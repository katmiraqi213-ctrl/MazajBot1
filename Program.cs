using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;

namespace MazajBot
{
    public class Program
    {
        private static IWolfClient? _client;
        private static MazajGame? _game;

        private static readonly HashSet<string> _processedMessages = new();
        private static readonly object _messageLock = new();

        public static async Task Main(string[] args)
        {
            string email =
                Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";

            string password =
                Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine(
                    "❌ WOLF_EMAIL أو WOLF_PASSWORD غير موجود."
                );

                return;
            }

            Console.WriteLine("🚀 تشغيل Mazaj Bot...");

            _client = new WolfClient();

            _client.OnConnected += () =>
            {
                Console.WriteLine("✅ تم الاتصال بـ Wolf.");
            };

            _client.OnDisconnected += (ex) =>
            {
                Console.WriteLine("⚠️ انقطع الاتصال.");
            };

            _client.OnConnectionError += (ex) =>
            {
                Console.WriteLine(
                    "❌ Connection Error: " + ex.Message
                );
            };

            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(message.MessageId))
                        return;

                    lock (_messageLock)
                    {
                        if (_processedMessages.Contains(message.MessageId))
                            return;

                        _processedMessages.Add(message.MessageId);

                        if (_processedMessages.Count > 5000)
                            _processedMessages.Clear();
                    }

                    string text =
                        message.Content?.Trim() ?? "";

                    // ==========================================
                    // الرقم المباشر
                    // ==========================================
                    if (TryParseNumber(text, out int number))
                    {
                        // قبل بدء اللعبة: تجاهل الرقم تماماً
                        if (_game == null || !_game.Started)
                            return;

                        // بعد بدء اللعبة: الرقم يعمل فقط لصاحب الدور
                        await ChooseCard(
                            client,
                            message,
                            number,
                            true
                        );

                        return;
                    }

                    // ==========================================
                    // أوامر مزاج
                    // ==========================================
                    if (!text.StartsWith(
                        "!مزاج",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    string command =
                        text.Length > 5
                            ? text.Substring(5).Trim()
                            : "";

                    await HandleCommand(
                        client,
                        message,
                        command
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        "COMMAND ERROR: " + ex.Message
                    );
                }
            };

            // تسجيل الدخول
            bool loginResult =
                await _client.Login(email, password);

            if (!loginResult)
            {
                Console.WriteLine("❌ فشل تسجيل الدخول إلى Wolf.");
                return;
            }

            Console.WriteLine("✅ تم تسجيل الدخول.");

            await _client.Connect();

            await Task.Delay(Timeout.Infinite);
        }

        // =========================================================
        // الأوامر
        // =========================================================

        private static async Task HandleCommand(
            IWolfClient client,
            Message message,
            string command)
        {
            string[] parts =
                command.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );

            if (parts.Length == 0)
            {
                await SendHelp(client, message);
                return;
            }

            string action =
                parts[0].ToLowerInvariant();

            switch (action)
            {
                case "جديد":
                    await NewGame(client, message);
                    break;

                case "انضم":
                    await JoinTeam(client, message, parts);
                    break;

                case "تغيير":
                    await ChangeTeam(client, message, parts);
                    break;

                case "لاعبين":
                    await ShowPlayers(client, message);
                    break;

                case "بدء":
                    await StartGame(client, message);
                    break;

                case "اختار":

                    if (parts.Length >= 2 &&
                        TryParseNumber(
                            parts[1],
                            out int number))
                    {
                        await ChooseCard(
                            client,
                            message,
                            number,
                            false
                        );
                    }
                    else
                    {
                        await client.Reply(
                            message,
                            "❌ استخدم:\n!مزاج اختار <رقم>"
                        );
                    }

                    break;

                case "بطاقات":
                    await ShowCards(client, message);
                    break;

                case "انهاء":
                    await EndGame(client, message);
                    break;

                case "مساعدة":
                case "help":
                    await SendHelp(client, message);
                    break;

                default:
                    await client.Reply(
                        message,
                        "❌ أمر غير معروف.\n" +
                        "اكتب !مزاج مساعدة"
                    );
                    break;
            }
        }

        // =========================================================
        // إنشاء لعبة
        // =========================================================

        private static async Task NewGame(
            IWolfClient client,
            Message message)
        {
            if (_game != null)
            {
                await client.Reply(
                    message,
                    "⚠️ توجد لعبة حالياً.\n" +
                    "استخدم !مزاج انهاء أولاً."
                );

                return;
            }

            _game = new MazajGame();

            _game.GroupId =
                message.GroupId ?? "";

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج!\n\n" +
                "🟥 الأحمر\n" +
                "🟦 الأزرق\n\n" +
                "💰 البداية: 400 نقطة لكل فريق\n" +
                "🎴 البطاقات: 65\n" +
                "⏱️ وقت الدور: 25 ثانية\n\n" +
                "📌 الانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n\n" +
                "▶️ وبعدها:\n" +
                "!مزاج بدء"
            );
        }

        // =========================================================
        // الانضمام
        // =========================================================

        private static async Task JoinTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ اللعبة بدأت بالفعل."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام:\n" +
                    "!مزاج انضم <احمر|ازرق>"
                );

                return;
            }

            string teamName =
                NormalizeTeam(parts[1]);

            Team? team =
                _game.Teams.FirstOrDefault(
                    x => x.Name == teamName
                );

            if (team == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود."
                );

                return;
            }

            string userId =
                message.UserId;

            foreach (Team existingTeam in _game.Teams)
            {
                if (existingTeam.Players.ContainsKey(userId))
                {
                    await client.Reply(
                        message,
                        $"⚠️ أنت منضم مسبقاً إلى " +
                        $"{existingTeam.Emoji} {existingTeam.Name}."
                    );

                    return;
                }
            }

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            team.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم انضمامك إلى " +
                $"{team.Emoji} {team.Name}\n" +
                $"👤 {nickname}"
            );
        }

        // =========================================================
        // تغيير الفريق
        // =========================================================

        private static async Task ChangeTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة."
                );

                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ لا يمكن تغيير الفريق بعد بدء اللعبة."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام:\n" +
                    "!مزاج تغيير <احمر|ازرق>"
                );

                return;
            }

            string userId =
                message.UserId;

            string newTeamName =
                NormalizeTeam(parts[1]);

            Team? newTeam =
                _game.Teams.FirstOrDefault(
                    x => x.Name == newTeamName
                );

            if (newTeam == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود."
                );

                return;
            }

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            foreach (Team team in _game.Teams)
            {
                if (team.Players.Remove(userId))
                {
                    newTeam.Players[userId] =
                        nickname;

                    await client.Reply(
                        message,
                        $"🔄 تم تغيير فريقك إلى " +
                        $"{newTeam.Emoji} {newTeam.Name}"
                    );

                    return;
                }
            }

            newTeam.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم تسجيلك في " +
                $"{newTeam.Emoji} {newTeam.Name}"
            );
        }

        // =========================================================
        // اللاعبين
        // =========================================================

        private static async Task ShowPlayers(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة."
                );

                return;
            }

            string result =
                "👥 لاعبو لعبة مزاج\n\n";

            foreach (Team team in _game.Teams)
            {
                result +=
                    $"{team.Emoji} {team.Name}\n";

                if (team.Players.Count == 0)
                {
                    result +=
                        "   لا يوجد لاعبين\n";
                }
                else
                {
                    int index = 1;

                    foreach (
                        string name
                        in team.Players.Values)
                    {
                        result +=
                            $"   {index}. {name}\n";

                        index++;
                    }
                }

                result += "\n";
            }

            await client.Reply(
                message,
                result.TrimEnd()
            );
        }

        // =========================================================
        // بدء اللعبة
        // =========================================================

        private static async Task StartGame(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة."
                );

                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "⚠️ اللعبة بدأت بالفعل."
                );

                return;
            }

            // لازم لاعب واحد على الأقل بكل فريق
            if (_game.Teams.Any(
                x => x.Players.Count == 0))
            {
                await client.Reply(
                    message,
                    "❌ لازم يكون بكل فريق لاعب واحد على الأقل."
                );

                return;
            }

            _game.TurnOrder.Clear();

            foreach (Team team in _game.Teams)
            {
                foreach (string userId
                         in team.Players.Keys)
                {
                    _game.TurnOrder.Add(userId);
                }
            }

            if (_game.TurnOrder.Count == 0)
            {
                await client.Reply(
                    message,
                    "❌ لا يوجد لاعبون."
                );

                return;
            }

            _game.Started = true;
            _game.CurrentPlayerIndex = 0;
            _game.TurnVersion++;

            string result =
                "🎭🔥 مــــزاج 🔥🎭\n\n" +
                BuildScoreBoard(_game) +
                "\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(_game) +
                "\n\n" +
                $"👤 الدور: {_game.CurrentPlayerName}\n" +
                "⏱️ 25ث";

            await client.Reply(
                message,
                result
            );

            _ = StartTurnTimer(
                client,
                _game
            );
        }

        // =========================================================
        // اختيار البطاقة
        // =========================================================

        private static async Task ChooseCard(
            IWolfClient client,
            Message message,
            int number,
            bool directNumber)
        {
            if (_game == null ||
                !_game.Started)
            {
                return;
            }

            MazajGame game = _game;

            // الرقم خارج 1-65
            if (number < 1 || number > 65)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ الرقم من 1 إلى 65."
                    );
                }

                return;
            }

            string userId =
                message.UserId;

            // غير مشارك
            if (!game.TurnOrder.Contains(userId))
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ أنت لست مشاركاً."
                    );
                }

                return;
            }

            // ليس دوره
            if (game.CurrentPlayerId != userId)
            {
                // الرقم المباشر يتم تجاهله بصمت
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        $"⏳ ليس دورك.\n" +
                        $"👤 الدور: {game.CurrentPlayerName}"
                    );
                }

                return;
            }

            Card? card =
                game.Cards.FirstOrDefault(
                    x => x.Number == number
                );

            if (card == null)
                return;

            // البطاقة مستخدمة
            if (card.Used)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ هذه البطاقة مستخدمة."
                    );
                }

                return;
            }

            Team? playerTeam =
                game.GetTeamByPlayer(userId);

            if (playerTeam == null)
                return;

            // تسجيل البطاقة
            card.Used = true;

            int oldScore =
                playerTeam.Score;

            // ==========================================
            // الموجب:
            // ينقص من الفريق المنافس
            // ==========================================
            if (card.Value > 0)
            {
                foreach (Team enemy
                         in game.Teams)
                {
                    if (enemy.Name != playerTeam.Name)
                    {
                        enemy.Score -=
                            card.Value;

                        if (enemy.Score < 0)
                            enemy.Score = 0;
                    }
                }
            }

            // ==========================================
            // السالب:
            // ينقص من فريق اللاعب نفسه
            // ==========================================
            else
            {
                playerTeam.Score +=
                    card.Value;

                if (playerTeam.Score < 0)
                    playerTeam.Score = 0;
            }

            game.TurnVersion++;

            string effect;

            if (card.Value > 0)
            {
                effect =
                    $"💥 -{card.Value} من الفريق المنافس";
            }
            else
            {
                effect =
                    $"💔 -{Math.Abs(card.Value)} من فريق اللاعب";
            }

            // ==========================================
            // فوز / خسارة
            // ==========================================

            Team? loser =
                game.Teams.FirstOrDefault(
                    x => x.Score <= 0
                );

            if (loser != null)
            {
                Team? winner =
                    game.Teams.FirstOrDefault(
                        x => x.Name != loser.Name
                    );

                string result =
                    "🎭💍 مــــزاج\n\n" +
                    $"🎴 البطاقة رقم {card.Number}\n" +
                    $"🃏 {card.Name}\n" +
                    $"💰 {FormatValue(card.Value)}\n" +
                    $"{effect}\n\n" +
                    BuildScoreBoard(game) +
                    "\n\n" +
                    "🎴 لوحة الأرقام\n" +
                    BuildCardBoard(game) +
                    "\n\n" +
                    "🏁 انتهت اللعبة!\n" +
                    $"💀 الخاسر: {loser.Emoji} {loser.Name}\n";

                if (winner != null)
                {
                    result +=
                        $"👑 الفائز: {winner.Emoji} {winner.Name}";
                }

                game.Started = false;
                game.TurnVersion++;

                _game = null;

                await client.Reply(
                    message,
                    result
                );

                return;
            }

            // ==========================================
            // كل البطاقات مستخدمة
            // ==========================================

            if (game.AllCardsUsed)
            {
                game.Started = false;
                game.TurnVersion++;

                string result =
                    "🎭💍 مــــزاج\n\n" +
                    $"🎴 البطاقة رقم {card.Number}\n" +
                    $"🃏 {card.Name}\n" +
                    $"💰 {FormatValue(card.Value)}\n" +
                    $"{effect}\n\n" +
                    BuildScoreBoard(game) +
                    "\n\n" +
                    "🎴 لوحة الأرقام\n" +
                    BuildCardBoard(game) +
                    "\n\n" +
                    "🏁 انتهت جميع البطاقات!\n\n" +
                    BuildFinalResults(game);

                _game = null;

                await client.Reply(
                    message,
                    result
                );

                return;
            }

            // ==========================================
            // الدور التالي
            // ==========================================

            game.CurrentPlayerIndex++;

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            string normalResult =
                "🎭💍 مــــزاج\n\n" +
                $"🎴 البطاقة رقم {card.Number}\n" +
                $"🃏 {card.Name}\n" +
                $"💰 {FormatValue(card.Value)}\n" +
                $"{effect}\n\n" +
                BuildScoreBoard(game) +
                "\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(game) +
                "\n\n" +
                $"👤 الدور: {nextPlayer}\n" +
                "⏱️ 25ث";

            await client.Reply(
                message,
                normalResult
            );

            _ = StartTurnTimer(
                client,
                game
            );
        }

        // =========================================================
        // مؤقت 25 ثانية
        // =========================================================

        private static async Task StartTurnTimer(
            IWolfClient client,
            MazajGame game)
        {
            int version =
                game.TurnVersion;

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(25)
                );

                if (_game != game ||
                    !game.Started ||
                    game.TurnVersion != version)
                {
                    return;
                }

                string oldPlayer =
                    game.CurrentPlayerName;

                game.TurnVersion++;

                game.CurrentPlayerIndex++;

                if (game.CurrentPlayerIndex >=
                    game.TurnOrder.Count)
                {
                    game.CurrentPlayerIndex = 0;
                }

                string nextPlayer =
                    game.CurrentPlayerName;

                string result =
                    "⏰ انتهى الوقت\n\n" +
                    $"👤 اللاعب: {oldPlayer}\n" +
                    "🚫 لم يتم اختيار بطاقة.\n\n" +
                    BuildScoreBoard(game) +
                    "\n\n" +
                    "🎴 لوحة الأرقام\n" +
                    BuildCardBoard(game) +
                    "\n\n" +
                    $"👤 الدور: {nextPlayer}\n" +
                    "⏱️ 25ث";

                if (!string.IsNullOrWhiteSpace(
                    game.GroupId))
                {
                    await client.GroupMessage(
                        game.GroupId,
                        result
                    );
                }

                _ = StartTurnTimer(
                    client,
                    game
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "TIMER ERROR: " +
                    ex.Message
                );
            }
        }

        // =========================================================
        // إنهاء اللعبة
        // =========================================================

        private static async Task EndGame(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة."
                );

                return;
            }

            MazajGame game = _game;

            game.Started = false;
            game.TurnVersion++;

            string result =
                "🛑 تم إنهاء لعبة مزاج.\n\n" +
                BuildFinalResults(game);

            _game = null;

            await client.Reply(
                message,
                result
            );
        }

        // =========================================================
        // لوحة النتائج الصغيرة
        // =========================================================

        private static string BuildScoreBoard(
            MazajGame game)
        {
            string result =
                "   💍 مزاج\n" +
                "┌────────┐\n";

            foreach (Team team in game.Teams)
            {
                string score =
                    team.Score.ToString();

                string line =
                    $"{team.Emoji} {score}";

                result +=
                    $"│{CenterText(line, 8)}│\n";
            }

            result +=
                "└────────┘";

            return result;
        }

        // =========================================================
        // توسيط النص
        // =========================================================

        private static string CenterText(
            string text,
            int width)
        {
            if (text.Length >= width)
                return text.Substring(0, width);

            int left =
                (width - text.Length) / 2;

            int right =
                width - text.Length - left;

            return
                new string(' ', left) +
                text +
                new string(' ', right);
        }

        // =========================================================
        // لوحة الأرقام
        // =========================================================

        private static string BuildCardBoard(
            MazajGame game)
        {
            string result =
                "💍 مــــزاج\n\n";

            for (int i = 1; i <= 65; i++)
            {
                Card card =
                    game.Cards[i - 1];

                string display =
                    card.Used
                        ? "❌"
                        : i.ToString();

                result += display.PadLeft(3);

                if (i % 8 == 0)
                {
                    result += "\n";
                }
                else
                {
                    result += " ";
                }
            }

            return result.TrimEnd();
        }

        // =========================================================
        // عرض البطاقات
        // =========================================================

        private static async Task ShowCards(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(
                    message,
                    "❌ لا توجد لعبة."
                );

                return;
            }

            string result =
                "🎴 بطاقات لعبة مزاج\n\n";

            foreach (Card card in _game.Cards)
            {
                result +=
                    $"{card.Number}. " +
                    $"{card.Name} " +
                    $"({FormatValue(card.Value)})\n";
            }

            await client.Reply(
                message,
                result
            );
        }

        // =========================================================
        // المساعدة
        // =========================================================

        private static async Task SendHelp(
            IWolfClient client,
            Message message)
        {
            string help =
                "🎭🔥 أوامر لعبة مزاج\n\n" +

                "🎮 إنشاء اللعبة:\n" +
                "!مزاج جديد\n\n" +

                "👥 الانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n\n" +

                "🔄 تغيير الفريق:\n" +
                "!مزاج تغيير احمر\n" +
                "!مزاج تغيير ازرق\n\n" +

                "👥 اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +

                "▶️ بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +

                "🎴 اختيار البطاقة:\n" +
                "اكتب الرقم مباشرة\n" +
                "مثال: 13\n\n" +

                "أو:\n" +
                "!مزاج اختار 13\n\n" +

                "🃏 البطاقات:\n" +
                "!مزاج بطاقات\n\n" +

                "🛑 إنهاء:\n" +
                "!مزاج انهاء\n\n" +

                "⏱️ وقت الدور: 25 ثانية\n" +
                "💰 بداية كل فريق: 400";

            await client.Reply(
                message,
                help
            );
        }

        // =========================================================
        // النتائج النهائية
        // =========================================================

        private static string BuildFinalResults(
            MazajGame game)
        {
            List<Team> ranking =
                game.Teams
                    .OrderByDescending(x => x.Score)
                    .ToList();

            string result =
                "🏆 النتائج النهائية\n\n";

            int place = 1;

            foreach (Team team in ranking)
            {
                result +=
                    $"{place}. " +
                    $"{team.Emoji} {team.Name} " +
                    $"— {team.Score}\n";

                place++;
            }

            if (ranking.Count > 0)
            {
                Team winner =
                    ranking[0];

                result +=
                    $"\n👑 الفائز: " +
                    $"{winner.Emoji} {winner.Name}";
            }

            return result;
        }

        // =========================================================
        // أسماء الفرق
        // =========================================================

        private static string NormalizeTeam(
            string value)
        {
            return value.Trim()
                .ToLowerInvariant() switch
            {
                "احمر" => "احمر",
                "الأحمر" => "احمر",
                "ازرق" => "ازرق",
                "الأزرق" => "ازرق",
                "اصفر" => "اصفر",
                "الأصفر" => "اصفر",
                "بنفسجي" => "بنفسجي",
                "البنفسجي" => "بنفسجي",
                _ => value.Trim()
            };
        }

        // =========================================================
        // قراءة الرقم
        // =========================================================

        private static bool TryParseNumber(
            string text,
            out int number)
        {
            number = 0;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            return int.TryParse(
                text.Trim(),
                out number
            );
        }

        // =========================================================
        // تنسيق القيمة
        // =========================================================

        private static string FormatValue(
            int value)
        {
            return value >= 0
                ? $"+{value}"
                : value.ToString();
        }

        // =========================================================
        // اسم اللاعب
        // =========================================================

        private static async Task<string> GetNickname(
            IWolfClient client,
            string userId)
        {
            try
            {
                var user =
                    await client.GetUser(userId);

                if (user != null &&
                    !string.IsNullOrWhiteSpace(
                        user.Nickname))
                {
                    return user.Nickname;
                }
            }
            catch
            {
            }

            return userId;
        }
    }

    // =================================================================
    // MazajGame
    // =================================================================

    public class MazajGame
    {
        public int PointsPerCard { get; } = 0;

        public int TeamCount { get; } = 2;

        public List<Team> Teams { get; }

        public List<Card> Cards { get; }

        public List<string> TurnOrder { get; }

        public bool Started { get; set; }

        public int CurrentPlayerIndex { get; set; }

        public int TurnVersion { get; set; }

        public string GroupId { get; set; } = "";

        public MazajGame()
        {
            Teams =
                new List<Team>
                {
                    new Team("احمر", "🟥"),
                    new Team("ازرق", "🟦")
                };

            TurnOrder =
                new List<string>();

            Cards =
                CreateCards();

            Started = false;
            CurrentPlayerIndex = 0;
            TurnVersion = 0;
        }

        // -------------------------------------------------------------
        // اللاعب الحالي
        // -------------------------------------------------------------

        public string CurrentPlayerId
        {
            get
            {
                if (TurnOrder.Count == 0)
                    return "";

                if (CurrentPlayerIndex < 0 ||
                    CurrentPlayerIndex >= TurnOrder.Count)
                    return "";

                return TurnOrder[
                    CurrentPlayerIndex
                ];
            }
        }

        public string CurrentPlayerName
        {
            get
            {
                string userId =
                    CurrentPlayerId;

                if (string.IsNullOrWhiteSpace(userId))
                    return "";

                foreach (Team team in Teams)
                {
                    if (team.Players.TryGetValue(
                        userId,
                        out string? name))
                    {
                        return name;
                    }
                }

                return userId;
            }
        }

        // -------------------------------------------------------------
        // فريق اللاعب
        // -------------------------------------------------------------

        public Team? GetTeamByPlayer(
            string userId)
        {
            return Teams.FirstOrDefault(
                x => x.Players.ContainsKey(userId)
            );
        }

        // -------------------------------------------------------------
        // هل انتهت البطاقات؟
        // -------------------------------------------------------------

        public bool AllCardsUsed
        {
            get
            {
                return Cards.All(
                    x => x.Used
                );
            }
        }

        // -------------------------------------------------------------
        // إنشاء 65 بطاقة
        // 57 موجبة + 8 سالبة
        // -------------------------------------------------------------

        private static List<Card> CreateCards()
        {
            var definitions =
                new List<(string Name, int Value)>
                {
                    // =====================================
                    // 57 بطاقة موجبة
                    // =====================================

                    ("ضربة الوحش محمد 🇮🇶❤️", 100),
                    ("ضربة يوسف المهندس", 90),
                    ("ضربة سرمد الوحش 🔥", 85),
                    ("ضربة سند سوريا", 75),

                    ("ضربة ابو عماد", 70),
                    ("ضربة حمدي الوزير", 70),
                    ("ضربة حيدر بنكه", 68),
                    ("ضربة جمو موسيقى", 68),
                    ("ضربة اساور صاروخ باليستي", 66),
                    ("صاروخ ارض ارض", 66),
                    ("ضربة علي القويه", 64),
                    ("ضربة ابو جنه", 64),
                    ("ضربة مزاج", 62),
                    ("حظك اليوم", 62),
                    ("المفاجأة", 60),
                    ("ضربة الحظ", 60),
                    ("البطاقة الغامضة", 58),
                    ("ضربة قوية", 58),
                    ("ضربة خفيفة", 56),
                    ("الحظ الجميل", 56),
                    ("مفاجأة مزاج", 54),
                    ("الضربة الأخيرة", 54),
                    ("ضربة البرق", 52),
                    ("ضربة النار", 52),
                    ("ضربة الصدمة", 50),
                    ("الضربة السرية", 50),
                    ("بطاقة الحظ", 48),
                    ("مفاجأة الفريق", 48),
                    ("الضربة الكبرى", 46),
                    ("ضربة الأسد", 46),
                    ("ضربة الصقر", 44),
                    ("ضربة الملك", 44),
                    ("ضربة الأمير", 42),
                    ("ضربة النمر", 42),
                    ("ضربة الذهب", 40),
                    ("ضربة الماس", 40),
                    ("ضربة الرعد", 38),
                    ("ضربة البركان", 38),
                    ("ضربة السيف", 36),
                    ("ضربة القائد", 36),
                    ("ضربة الأبطال", 34),
                    ("ضربة المفاجآت", 34),
                    ("ضربة السريعة", 32),
                    ("ضربة النار الحمراء", 32),
                    ("ضربة القوة", 30),
                    ("ضربة التركيز", 30),
                    ("ضربة الفوز", 28),
                    ("ضربة الفرصة", 28),
                    ("ضربة الحماس", 26),
                    ("ضربة الجمهور", 26),
                    ("ضربة الذكاء", 24),
                    ("ضربة الفراسة", 24),
                    ("ضربة الحظ السعيد", 22),
                    ("ضربة المزاج", 20),
                    ("ضربة النهاية", 15),

                    // =====================================
                    // 8 بطاقات سالبة فقط
                    // =====================================

                    ("هولو وئام الفگر", -60),
                    ("طاحج حضج توت 😂", -50),
                    ("صخام بوجهك ايهاب", -45),
                    ("سراوي تيتي لاتحل ولا تربط", -40),
                    ("هذا حظ زوز", -35),
                    ("لولو التعبانه", -30),
                    ("نواره السلبيه", -25),
                    ("بطاقة النحس", -20)
                };

            if (definitions.Count != 65)
            {
                throw new Exception(
                    $"خطأ: عدد البطاقات = {definitions.Count}"
                );
            }

            int positiveCount =
                definitions.Count(x => x.Value > 0);

            int negativeCount =
                definitions.Count(x => x.Value < 0);

            if (positiveCount != 57)
            {
                throw new Exception(
                    $"خطأ: البطاقات الموجبة = {positiveCount}"
                );
            }

            if (negativeCount != 8)
            {
                throw new Exception(
                    $"خطأ: البطاقات السالبة = {negativeCount}"
                );
            }

            if (definitions.Any(x => x.Value > 100))
            {
                throw new Exception(
                    "خطأ: توجد بطاقة أعلى من +100."
                );
            }

            Random random =
                new Random();

            definitions =
                definitions
                    .OrderBy(x => random.Next())
                    .ToList();

            List<Card> cards =
                new List<Card>();

            for (int i = 0;
                 i < definitions.Count;
                 i++)
            {
                cards.Add(
                    new Card(
                        i + 1,
                        definitions[i].Name,
                        definitions[i].Value
                    )
                );
            }

            return cards;
        }
    }

    // =================================================================
    // Team
    // =================================================================

    public class Team
    {
        public string Name { get; }

        public string Emoji { get; }

        // البداية 400
        public int Score { get; set; }

        public Dictionary<string, string> Players { get; }

        public Team(
            string name,
            string emoji)
        {
            Name = name;
            Emoji = emoji;

            Score = 400;

            Players =
                new Dictionary<string, string>();
        }
    }

    // =================================================================
    // Card
    // =================================================================

    public class Card
    {
        public int Number { get; }

        public string Name { get; }

        public int Value { get; }

        public bool Used { get; set; }

        public Card(
            int number,
            string name,
            int value)
        {
            Number = number;
            Name = name;
            Value = value;
            Used = false;
        }
    }
}
