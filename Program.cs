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

        // منع معالجة نفس الرسالة أكثر من مرة
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

            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    // منع التكرار
                    string messageKey =
                        !string.IsNullOrWhiteSpace(message.MessageId)
                            ? message.MessageId
                            : $"{message.UserId}|{message.GroupId}|{message.Content}|{message.Timestamp.Ticks}";

                    lock (_messageLock)
                    {
                        if (_processedMessages.Contains(messageKey))
                        {
                            return;
                        }

                        _processedMessages.Add(messageKey);

                        // منع نمو الذاكرة بشكل غير محدود
                        if (_processedMessages.Count > 5000)
                        {
                            _processedMessages.Clear();
                        }
                    }

                    string text =
                        message.Content?.Trim() ?? "";

                    // ==========================================
                    // الرقم المباشر
                    // ==========================================

                    if (TryParseNumber(text, out int directNumber))
                    {
                        // قبل بدء اللعبة يتم تجاهل الرقم
                        if (_game != null && _game.Started)
                        {
                            await ChooseCard(
                                client,
                                message,
                                directNumber,
                                true
                            );
                        }

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
                        "❌ COMMAND ERROR: " + ex
                    );
                }
            };

            // تسجيل الدخول
            bool loginResult =
                await _client.Login(email, password);

            if (!loginResult)
            {
                Console.WriteLine(
                    "❌ فشل تسجيل الدخول إلى Wolf."
                );

                return;
            }

            Console.WriteLine(
                "✅ تم تسجيل الدخول."
            );

            await _client.Connect();

            Console.WriteLine(
                "🚀 البوت يعمل الآن."
            );

            await Task.Delay(
                Timeout.Infinite
            );
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
                    await JoinTeam(
                        client,
                        message,
                        parts
                    );
                    break;

                case "تغيير":
                    await ChangeTeam(
                        client,
                        message,
                        parts
                    );
                    break;

                case "لاعبين":
                    await ShowPlayers(
                        client,
                        message
                    );
                    break;

                case "بدء":
                    await StartGame(
                        client,
                        message
                    );
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
                            "❌ الاستخدام:\n!مزاج اختار <رقم>"
                        );
                    }

                    break;

                case "بطاقات":
                    await ShowCards(
                        client,
                        message
                    );
                    break;

                case "انهاء":
                    await EndGame(
                        client,
                        message
                    );
                    break;

                case "مساعدة":
                case "help":
                    await SendHelp(
                        client,
                        message
                    );
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

            // صاحب اللعبة يدخل تلقائياً بالأحمر
            string userId =
                message.UserId;

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            Team redTeam =
                _game.Teams[0];

            redTeam.Players[userId] =
                nickname;

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج!\n\n" +
                "💰 البداية: 400 نقطة لكل فريق\n" +
                "🎴 البطاقات: 65\n" +
                "🟢 البطاقات الموجبة: 57\n" +
                "🔴 البطاقات السالبة: 8\n" +
                "⏱️ وقت الدور: 25 ثانية\n\n" +
                "👤 أنت دخلت تلقائياً:\n" +
                "🟥 الفريق الأحمر\n\n" +
                "📌 الانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n\n" +
                "📌 تغيير الفريق:\n" +
                "!مزاج تغيير ازرق\n\n" +
                "📌 بعد اكتمال الفريقين:\n" +
                "!مزاج بدء"
            );
        }

        // =========================================================
        // الانضمام للفريق
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
                    "!مزاج انضم احمر\n" +
                    "!مزاج انضم ازرق"
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
                    "❌ الفريق غير موجود.\n" +
                    "المتاح: احمر أو ازرق"
                );

                return;
            }

            string userId =
                message.UserId;

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            // إذا موجود مسبقاً
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

            team.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم انضمامك إلى " +
                $"{team.Emoji} {team.Name}\n" +
                $"👤 اللاعب: {nickname}"
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
                    "❌ لا توجد لعبة حالياً."
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
                    "!مزاج تغيير احمر\n" +
                    "!مزاج تغيير ازرق"
                );

                return;
            }

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

            string userId =
                message.UserId;

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
        // عرض اللاعبين
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

            // لازم كل فريق بيه لاعب
            if (_game.Teams.Any(
                x => x.Players.Count == 0))
            {
                await client.Reply(
                    message,
                    "❌ يجب أن يكون في كل فريق لاعب واحد على الأقل.\n\n" +
                    "🟥 الأحمر: " +
                    $"{_game.Teams[0].Players.Count}\n" +
                    "🟦 الأزرق: " +
                    $"{_game.Teams[1].Players.Count}"
                );

                return;
            }

            _game.TurnOrder.Clear();

            // ترتيب اللاعبين حسب الفريق
            foreach (Team team in _game.Teams)
            {
                foreach (
                    string userId
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

            string firstPlayer =
                _game.CurrentPlayerName;

            string result =
                "🎭🔥 مــــزاج 🔥🎭\n\n" +
                "🏁 بدأت اللعبة!\n\n" +
                BuildScoreBoard(_game) +
                "\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(_game) +
                "\n\n" +
                $"👤 الدور على: {firstPlayer}\n" +
                "⏱️ عندك 25 ثانية";

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

            MazajGame game =
                _game;

            // الرقم خارج المدى
            if (number < 1 || number > 65)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ رقم البطاقة يجب أن يكون من 1 إلى 65."
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
                        "❌ أنت لست مشاركاً في اللعبة."
                    );
                }

                return;
            }

            // ليس دوره
            if (game.CurrentPlayerId != userId)
            {
                // الرقم المباشر يتجاهل بصمت
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        $"⏳ ليس دورك.\n" +
                        $"👤 الدور حالياً: {game.CurrentPlayerName}"
                    );
                }

                return;
            }

            Card? card =
                game.Cards.FirstOrDefault(
                    x => x.Number == number
                );

            if (card == null)
            {
                return;
            }

            // البطاقة مستخدمة
            if (card.Used)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ هذه البطاقة مستخدمة مسبقاً."
                    );
                }

                return;
            }

            Team? playerTeam =
                game.GetTeamByPlayer(userId);

            if (playerTeam == null)
            {
                return;
            }

            // تسجيل البطاقة
            card.Used = true;

            string effectMessage;

            if (card.Value > 0)
            {
                // الموجب ينقص نقاط الخصم
                foreach (Team team in game.Teams)
                {
                    if (team != playerTeam)
                    {
                        team.Score -= card.Value;

                        if (team.Score < 0)
                        {
                            team.Score = 0;
                        }
                    }
                }

                effectMessage =
                    $"💥 {playerTeam.Emoji} " +
                    $"{playerTeam.Name} ضرب الخصم " +
                    $"بـ {card.Value} نقطة!";
            }
            else
            {
                // السالب ينقص نقاط فريق اللاعب
                int loss =
                    Math.Abs(card.Value);

                playerTeam.Score -= loss;

                if (playerTeam.Score < 0)
                {
                    playerTeam.Score = 0;
                }

                effectMessage =
                    $"💀 {playerTeam.Emoji} " +
                    $"{playerTeam.Name} خسر " +
                    $"{loss} نقطة!";
            }

            game.TurnVersion++;

            string result =
                "🎭💍 مــــزاج\n\n" +
                $"🎴 البطاقة رقم: {card.Number}\n" +
                $"🃏 {card.Name}\n" +
                $"💰 القيمة: {FormatValue(card.Value)}\n\n" +
                effectMessage +
                "\n\n" +
                BuildScoreBoard(game) +
                "\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(game);

            // =====================================================
            // فوز / خسارة
            // =====================================================

            Team? loser =
                game.Teams.FirstOrDefault(
                    x => x.Score <= 0
                );

            if (loser != null)
            {
                Team? winner =
                    game.Teams.FirstOrDefault(
                        x => x != loser
                    );

                game.Started = false;
                game.TurnVersion++;

                result +=
                    "\n\n🏁 انتهت اللعبة!\n\n" +
                    $"💀 الخاسر: {loser.Emoji} {loser.Name}\n";

                if (winner != null)
                {
                    result +=
                        $"👑 الفائز: {winner.Emoji} {winner.Name}\n";
                }

                result +=
                    "\n" +
                    BuildFinalResults(game);

                _game = null;

                await client.Reply(
                    message,
                    result
                );

                return;
            }

            // =====================================================
            // كل البطاقات انتهت
            // =====================================================

            if (game.AllCardsUsed)
            {
                game.Started = false;
                game.TurnVersion++;

                result +=
                    "\n\n🏁 انتهت كل البطاقات!\n\n" +
                    BuildFinalResults(game);

                _game = null;

                await client.Reply(
                    message,
                    result
                );

                return;
            }

            // =====================================================
            // اللاعب التالي
            // =====================================================

            game.CurrentPlayerIndex++;

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            result +=
                "\n\n" +
                $"👤 الدور التالي: {nextPlayer}\n" +
                "⏱️ عندك 25 ثانية";

            await client.Reply(
                message,
                result
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
                    "⏰ انتهى الوقت!\n\n" +
                    $"👤 اللاعب: {oldPlayer}\n" +
                    "🚫 لم يتم اختيار بطاقة.\n\n" +
                    BuildScoreBoard(game) +
                    "\n\n" +
                    "🎴 لوحة الأرقام\n" +
                    BuildCardBoard(game) +
                    "\n\n" +
                    $"👤 الدور التالي: {nextPlayer}\n" +
                    "⏱️ عندك 25 ثانية";

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
                    "❌ TIMER ERROR: " +
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
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            MazajGame game =
                _game;

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
                string line =
                    $"{team.Emoji} {team.Score}";

                result +=
                    $"│{CenterText(line, 8)}│\n";
            }

            result +=
                "└────────┘";

            return result;
        }

        // =========================================================
        // لوحة الأرقام الدائرية
        // =========================================================

        private static string BuildCardBoard(
            MazajGame game)
        {
            string[] lines =
            {
                "        01  02  03",
                "     04  05  06  07",
                "  08  09  10  11  12",
                "13  14  15  💍  16  17",
                "18  19  20  مــــزاج  21",
                "22  23  24  25  26  27",
                "  28  29  30  31  32",
                "     33  34  35  36",
                "  37  38  39  40  41",
                "42  43  44  45  46  47",
                "  48  49  50  51  52",
                "     53  54  55  56",
                "        57  58  59",
                "           60  61",
                "           62  63",
                "              64",
                "              65"
            };

            // إذا البطاقة مستخدمة نبدل رقمها بـ ❌
            for (int i = 1; i <= 65; i++)
            {
                if (game.Cards[i - 1].Used)
                {
                    string number =
                        i.ToString("00");

                    for (int x = 0; x < lines.Length; x++)
                    {
                        lines[x] =
                            lines[x].Replace(
                                number,
                                "❌ "
                            );
                    }
                }
            }

            return string.Join(
                "\n",
                lines
            );
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
                string status =
                    card.Used
                        ? "❌"
                        : "⭕";

                result +=
                    $"{status} {card.Number}. " +
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
                "🎭🔥 أوامر لعبة مــــزاج\n\n" +

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
                "اكتب الرقم مباشرة مثل:\n" +
                "13\n\n" +

                "أو:\n" +
                "!مزاج اختار 13\n\n" +

                "🃏 عرض البطاقات:\n" +
                "!مزاج بطاقات\n\n" +

                "🛑 إنهاء اللعبة:\n" +
                "!مزاج انهاء\n\n" +

                "💰 البداية: 400 نقطة\n" +
                "🎴 البطاقات: 65\n" +
                "⏱️ الدور: 25 ثانية";

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
            string result =
                "🏆 النتائج النهائية\n\n";

            List<Team> ranking =
                game.Teams
                    .OrderByDescending(
                        x => x.Score)
                    .ToList();

            int place = 1;

            foreach (Team team in ranking)
            {
                result +=
                    $"{place}. " +
                    $"{team.Emoji} " +
                    $"{team.Name} — " +
                    $"{team.Score} نقطة\n";

                place++;
            }

            if (ranking.Count > 0)
            {
                Team winner =
                    ranking[0];

                result +=
                    "\n👑 المتصدر: " +
                    $"{winner.Emoji} " +
                    $"{winner.Name}";
            }

            return result;
        }

        // =========================================================
        // أسماء الفرق
        // =========================================================

        private static string NormalizeTeam(
            string value)
        {
            return value
                .Trim()
                .ToLowerInvariant()
                switch
            {
                "احمر" => "احمر",
                "الأحمر" => "احمر",
                "الاحمر" => "احمر",

                "ازرق" => "ازرق",
                "الأزرق" => "ازرق",
                "الازرق" => "ازرق",

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
            {
                return false;
            }

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
        // توسيط النص
        // =========================================================

        private static string CenterText(
            string text,
            int width)
        {
            if (text.Length >= width)
            {
                return text.Substring(
                    0,
                    width
                );
            }

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
                // تجاهل الخطأ
            }

            return userId;
        }
    }

    // =================================================================
    // MazajGame
    // =================================================================

    public class MazajGame
    {
        public int StartingScore { get; } = 400;

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
                    new Team(
                        "احمر",
                        "🟥"
                    ),

                    new Team(
                        "ازرق",
                        "🟦"
                    )
                };

            TurnOrder =
                new List<string>();

            Cards =
                CreateCards();
        }

        // =========================================================
        // اللاعب الحالي
        // =========================================================

        public string CurrentPlayerId
        {
            get
            {
                if (TurnOrder.Count == 0)
                {
                    return "";
                }

                if (CurrentPlayerIndex < 0 ||
                    CurrentPlayerIndex >=
                    TurnOrder.Count)
                {
                    return "";
                }

                return
                    TurnOrder[
                        CurrentPlayerIndex
                    ];
            }
        }

        // =========================================================
        // اسم اللاعب الحالي
        // =========================================================

        public string CurrentPlayerName
        {
            get
            {
                string userId =
                    CurrentPlayerId;

                if (string.IsNullOrWhiteSpace(
                    userId))
                {
                    return "";
                }

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

        // =========================================================
        // فريق اللاعب
        // =========================================================

        public Team? GetTeamByPlayer(
            string userId)
        {
            return Teams.FirstOrDefault(
                x => x.Players.ContainsKey(
                    userId)
            );
        }

        // =========================================================
        // هل كل البطاقات مستخدمة؟
        // =========================================================

        public bool AllCardsUsed
        {
            get
            {
                return Cards.All(
                    x => x.Used
                );
            }
        }

        // =========================================================
        // إنشاء 65 بطاقة
        // 57 موجبة + 8 سالبة
        // =========================================================

        private static List<Card> CreateCards()
        {
            List<Card> cards =
                new List<Card>();

            // ---------------------------------------------------------
            // 57 بطاقة موجبة
            // أعلى قيمة = 100
            // ---------------------------------------------------------

            int[] positiveValues =
            {
                100,
                90,
                85,
                75,
                70,
                70,
                68,
                68,
                66,
                66,
                64,
                64,
                62,
                62,
                60,
                60,
                58,
                58,
                56,
                56,
                54,
                54,
                52,
                52,
                50,
                50,
                48,
                48,
                46,
                46,
                44,
                44,
                42,
                42,
                40,
                40,
                38,
                38,
                36,
                36,
                34,
                34,
                32,
                32,
                30,
                30,
                28,
                28,
                26,
                26,
                24,
                24,
                22,
                22,
                20,
                15,
                10
            };

            // ---------------------------------------------------------
            // 8 بطاقات سالبة
            // ---------------------------------------------------------

            int[] negativeValues =
            {
                -60,
                -50,
                -45,
                -40,
                -35,
                -30,
                -25,
                -20
            };

            List<string> positiveNames =
                new List<string>
                {
                    "ضربة الوحش محمد 🇮🇶❤️",
                    "ضربة يوسف المهندس",
                    "ضربة سرمد الوحش 🔥",
                    "ضربة سند سوريا",

                    "ضربة ابو عماد",
                    "ضربة حمدي الوزير",
                    "ضربة حيدر بنكه",
                    "ضربة جمو موسيقى",
                    "ضربة اساور صاروخ باليستي",
                    "صاروخ ارض ارض",
                    "ضربة علي القويه",
                    "ضربة ابو جنه",

                    "ضربة مزاج",
                    "حظك اليوم",
                    "المفاجأة",
                    "ضربة الحظ",
                    "البطاقة الغامضة",
                    "ضربة قوية",
                    "ضربة خفيفة",
                    "الحظ الجميل",
                    "مفاجأة مزاج",
                    "الضربة الأخيرة",
                    "ضربة البرق",
                    "ضربة النار",
                    "ضربة الصدمة",
                    "الضربة السرية",
                    "بطاقة الحظ",
                    "مفاجأة الفريق",
                    "الضربة الكبرى"
                };

            // إكمال أسماء الموجبة إلى 57
            while (
                positiveNames.Count <
                positiveValues.Length)
            {
                positiveNames.Add(
                    "بطاقة مزاج"
                );
            }

            // إضافة البطاقات الموجبة
            for (int i = 0;
                 i < positiveValues.Length;
                 i++)
            {
                cards.Add(
                    new Card(
                        0,
                        positiveNames[i],
                        positiveValues[i]
                    )
                );
            }

            // ---------------------------------------------------------
            // البطاقات السالبة
            // ---------------------------------------------------------

            string[] negativeNames =
            {
                "هولو وئام الفگر",
                "طاحج حضج توت 😂",
                "صخام بوجهك ايهاب",
                "سراوي تيتي لاتحل ولا تربط",
                "هذا حظ زوز",
                "لولو التعبانه",
                "نواره السلبيه",
                "بطاقة النحس"
            };

            for (int i = 0;
                 i < negativeValues.Length;
                 i++)
            {
                cards.Add(
                    new Card(
                        0,
                        negativeNames[i],
                        negativeValues[i]
                    )
                );
            }

            // التأكد من العدد
            if (cards.Count != 65)
            {
                throw new Exception(
                    $"خطأ: عدد البطاقات = {cards.Count} وليس 65."
                );
            }

            // التأكد من عدد الموجب
            int positiveCount =
                cards.Count(x => x.Value > 0);

            if (positiveCount != 57)
            {
                throw new Exception(
                    $"خطأ: البطاقات الموجبة = {positiveCount} وليس 57."
                );
            }

            // التأكد من عدد السالب
            int negativeCount =
                cards.Count(x => x.Value < 0);

            if (negativeCount != 8)
            {
                throw new Exception(
                    $"خطأ: البطاقات السالبة = {negativeCount} وليس 8."
                );
            }

            // التأكد أن الأعلى 100
            if (cards.Max(x => x.Value) != 100)
            {
                throw new Exception(
                    "خطأ: أعلى بطاقة يجب أن تكون +100."
                );
            }

            // خلط البطاقات
            Random random =
                new Random();

            cards =
                cards
                    .OrderBy(
                        _ => random.Next())
                    .ToList();

            // ترقيم 1 إلى 65
            for (int i = 0;
                 i < cards.Count;
                 i++)
            {
                cards[i].Number =
                    i + 1;
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

        public int Score { get; set; }

        public Dictionary<string, string> Players { get; }

        public Team(
            string name,
            string emoji)
        {
            Name = name;
            Emoji = emoji;

            // البداية 400
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
        public int Number { get; set; }

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
