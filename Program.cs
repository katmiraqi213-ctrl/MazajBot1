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

        private static readonly HashSet<string> _processedMessages =
            new HashSet<string>();

        private static readonly object _messageLock =
            new object();

        private static readonly Dictionary<string, DateTime> _recentMessages =
            new Dictionary<string, DateTime>();

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
                    if (IsDuplicateMessage(message))
                    {
                        return;
                    }

                    string text =
                        message.Content?.Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return;
                    }

                    // اختيار رقم مباشرة
                    if (TryParseNumber(text, out int directNumber))
                    {
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

                    if (!text.StartsWith(
                            "!مزاج",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    Console.WriteLine(
                        $"🎮 أمر مزاج: {text}"
                    );

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
                        "❌ COMMAND ERROR: " +
                        ex.Message
                    );
                }
            };

            Console.WriteLine(
                "🔐 تسجيل الدخول إلى Wolf..."
            );

            bool loginResult =
                await _client.Login(
                    email,
                    password
                );

            if (!loginResult)
            {
                Console.WriteLine(
                    "❌ فشل تسجيل الدخول."
                );

                return;
            }

            Console.WriteLine(
                "✅ تم تسجيل الدخول بنجاح."
            );

            await _client.Connect();

            Console.WriteLine(
                "✅ تم الاتصال بـ Wolf."
            );

            await _client.Messaging.Initialize();

            Console.WriteLine(
                "✅ Messaging initialized."
            );

            Console.WriteLine(
                "🟢 Mazaj Bot يعمل الآن."
            );

            await Task.Delay(
                Timeout.Infinite
            );
        }

        private static bool IsDuplicateMessage(
            Message message)
        {
            lock (_messageLock)
            {
                if (!string.IsNullOrWhiteSpace(
                        message.MessageId))
                {
                    if (_processedMessages.Contains(
                            message.MessageId))
                    {
                        return true;
                    }

                    _processedMessages.Add(
                        message.MessageId
                    );

                    if (_processedMessages.Count > 2000)
                    {
                        _processedMessages.Clear();
                    }
                }

                string fingerprint =
                    $"{message.GroupId}|{message.UserId}|{message.Content?.Trim()}";

                DateTime now =
                    DateTime.UtcNow;

                var oldKeys =
                    _recentMessages
                        .Where(x =>
                            (now - x.Value).TotalSeconds > 2)
                        .Select(x => x.Key)
                        .ToList();

                foreach (string key in oldKeys)
                {
                    _recentMessages.Remove(key);
                }

                if (_recentMessages.TryGetValue(
                        fingerprint,
                        out DateTime lastTime))
                {
                    if ((now - lastTime).TotalMilliseconds < 1200)
                    {
                        return true;
                    }
                }

                _recentMessages[fingerprint] =
                    now;

                return false;
            }
        }

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
                    await NewGame(client, message, parts);
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
                        TryParseNumber(parts[1], out int number))
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
                        "❌ أمر غير معروف.\nاكتب !مزاج مساعدة"
                    );
                    break;
            }
        }

        // =========================================================
        // إنشاء اللعبة
        // !مزاج جديد
        // صاحب الأمر يدخل الأحمر تلقائياً
        // =========================================================

        private static async Task NewGame(
            IWolfClient client,
            Message message,
            string[] parts)
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

            // القيم الافتراضية
            // 100 = أعلى قيمة ممكنة للبطاقات
            // 2 = عدد الفرق الافتراضي
            int maxCardValue = 100;
            int teamCount = 2;

            // دعم الصيغة القديمة أيضاً:
            // !مزاج جديد 50 2
            if (parts.Length >= 2)
            {
                if (!int.TryParse(
                        parts[1],
                        out maxCardValue))
                {
                    await client.Reply(
                        message,
                        "❌ استخدم:\n" +
                        "!مزاج جديد"
                    );

                    return;
                }
            }

            if (parts.Length >= 3)
            {
                if (!int.TryParse(
                        parts[2],
                        out teamCount))
                {
                    await client.Reply(
                        message,
                        "❌ عدد الفرق غير صحيح."
                    );

                    return;
                }
            }

            if (maxCardValue < 20)
            {
                maxCardValue = 20;
            }

            if (maxCardValue > 100)
            {
                maxCardValue = 100;
            }

            if (teamCount < 2 || teamCount > 4)
            {
                await client.Reply(
                    message,
                    "❌ عدد الفرق يجب أن يكون من 2 إلى 4."
                );

                return;
            }

            _game =
                new MazajGame(
                    maxCardValue,
                    teamCount
                );

            _game.GroupId =
                message.GroupId ?? "";

            if (!string.IsNullOrWhiteSpace(
                    _game.GroupId))
            {
                await client.Messaging.GroupMessageSubscribe(
                    _game.GroupId
                );
            }

            // =====================================================
            // دخول صاحب الأمر تلقائياً للفريق الأحمر
            // =====================================================

            string userId =
                message.UserId;

            string nickname =
                await GetNickname(
                    client,
                    userId
                );

            Team? redTeam =
                _game.Teams.FirstOrDefault(
                    x => x.Name == "احمر"
                );

            if (redTeam != null)
            {
                redTeam.Players[userId] =
                    nickname;
            }

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج!\n\n" +
                $"👑 {nickname} دخل تلقائياً إلى 🟥 الفريق الأحمر\n\n" +
                "❤️ رصيد كل فريق: 400 نقطة\n" +
                "🎴 عدد البطاقات: 65\n" +
                "💰 قيم البطاقات: من 20 إلى 100\n" +
                "🔥 أعلى بطاقة: 100 نقطة\n\n" +
                "📌 للانضمام إلى فريق:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n" +
                "!مزاج انضم اصفر\n" +
                "!مزاج انضم بنفسجي\n\n" +
                "▶️ بعد اكتمال اللاعبين:\n" +
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
                    "!مزاج انضم <احمر|ازرق|اصفر|بنفسجي>"
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
                    "❌ الفريق غير موجود أو غير متاح."
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
                    "!مزاج تغيير <احمر|ازرق|اصفر|بنفسجي>"
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

                    foreach (string name in team.Players.Values)
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

            _game.TurnOrder.Clear();

            foreach (Team team in _game.Teams)
            {
                foreach (string userId in team.Players.Keys)
                {
                    _game.TurnOrder.Add(userId);
                }
            }

            if (_game.TurnOrder.Count < 2)
            {
                await client.Reply(
                    message,
                    "❌ يجب أن ينضم لاعبان على الأقل."
                );

                return;
            }

            _game.Started = true;
            _game.CurrentPlayerIndex = 0;
            _game.TurnVersion++;

            string firstPlayer =
                _game.CurrentPlayerName;

            // لوحة النقاط وحدها
            await client.Reply(
                message,
                "🎭🔥 لعبة مزاج بدأت ⚡\n\n" +
                $"👥 مجموع اللاعبين: {_game.TurnOrder.Count}\n\n" +
                BuildScoreBoard(_game)
            );

            // لوحة الأرقام برسالة منفصلة
            await Task.Delay(
                TimeSpan.FromSeconds(1)
            );

            if (_game == null ||
                !_game.Started)
            {
                return;
            }

            string boardMessage =
                "🎴 لوحة الأرقام\n\n" +
                BuildCardBoard(_game) +
                "\n\n" +
                $"👤 اللاعب التالي: {firstPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await client.GroupMessage(
                _game.GroupId,
                boardMessage
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

            if (game.CurrentPlayerId != userId)
            {
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

            if (card.Used)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ هذه البطاقة تم اختيارها مسبقاً."
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

            card.Used = true;

            playerTeam.Score -=
                card.Value;

            game.TurnVersion++;

            bool teamLost =
                playerTeam.Score <= 0;

            if (playerTeam.Score < 0)
            {
                playerTeam.Score = 0;
            }

            string result =
                "/me\n\n" +
                $"🎴 تم اختيار البطاقة رقم {card.Number}\n\n" +
                $"🃏 البطاقة: {card.Name}\n" +
                $"💰 القيمة: -{card.Value}\n\n" +
                $"{playerTeam.Emoji} الفريق {playerTeam.Name} " +
                $"خسر {card.Value} نقطة\n\n" +
                BuildScoreBoard(game);

            // =====================================================
            // الفريق وصل صفر
            // =====================================================

            if (teamLost)
            {
                result +=
                    "\n\n" +
                    "💀━━━━━━━━━━━━💀\n" +
                    $"❌ الفريق {playerTeam.Name} خسر!\n" +
                    "وصل رصيده إلى 0 نقطة.\n" +
                    "🏁 انتهت اللعبة!\n" +
                    "💀━━━━━━━━━━━━💀\n\n" +
                    BuildFinalResults(game);

                game.Started = false;
                game.TurnVersion++;

                _game = null;

                await client.Reply(
                    message,
                    result
                );

                return;
            }

            // النتيجة وحدها
            await client.Reply(
                message,
                result
            );

            // انتظار ثانيتين
            await Task.Delay(
                TimeSpan.FromSeconds(2)
            );

            if (_game != game ||
                !game.Started)
            {
                return;
            }

            // =====================================================
            // إذا انتهت البطاقات
            // =====================================================

            if (game.AllCardsUsed)
            {
                game.Started = false;
                game.TurnVersion++;

                string finalResult =
                    "🏁 تم استخدام جميع البطاقات!\n\n" +
                    BuildFinalResults(game);

                _game = null;

                await client.GroupMessage(
                    game.GroupId,
                    finalResult
                );

                return;
            }

            // الدور التالي
            game.CurrentPlayerIndex++;

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            string boardMessage =
                "🎴 لوحة الأرقام\n\n" +
                BuildCardBoard(game) +
                "\n\n" +
                $"👤 اللاعب التالي: {nextPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await client.GroupMessage(
                game.GroupId,
                boardMessage
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
                    $"⏰ انتهى وقت اللاعب: {oldPlayer}\n\n" +
                    "🚫 لم يتم اختيار أي بطاقة.\n\n" +
                    BuildScoreBoard(game);

                await client.GroupMessage(
                    game.GroupId,
                    result
                );

                await Task.Delay(
                    TimeSpan.FromSeconds(2)
                );

                if (_game != game ||
                    !game.Started)
                {
                    return;
                }

                string boardMessage =
                    "🎴 لوحة الأرقام\n\n" +
                    BuildCardBoard(game) +
                    "\n\n" +
                    $"👤 اللاعب التالي: {nextPlayer}\n" +
                    "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

                await client.GroupMessage(
                    game.GroupId,
                    boardMessage
                );

                _ = StartTurnTimer(
                    client,
                    game
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "❌ TURN TIMER ERROR: " +
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
        // لوحة النقاط
        // =========================================================

        private static string BuildScoreBoard(
            MazajGame game)
        {
            string result =
                "📊 لوحة النقاط\n\n";

            foreach (Team team in game.Teams)
            {
                result +=
                    $"{team.Emoji} {team.Name}: " +
                    $"{team.Score} / 400\n";
            }

            return result.TrimEnd();
        }

        // =========================================================
        // لوحة الأرقام
        // =========================================================

        private static string BuildCardBoard(
            MazajGame game)
        {
            string result = "";

            for (int i = 1; i <= 65; i++)
            {
                Card card =
                    game.Cards[i - 1];

                string display =
                    card.Used
                        ? "❌"
                        : i.ToString().PadLeft(2, ' ');

                result += display;

                if (i < 65)
                {
                    if (i % 8 == 0)
                    {
                        result += "\n";
                    }
                    else
                    {
                        result += " | ";
                    }
                }
            }

            return result;
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
                    $"(-{card.Value})\n";
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

                "👑 صاحب الأمر يدخل تلقائياً:\n" +
                "🟥 الفريق الأحمر\n\n" +

                "❤️ رصيد كل فريق: 400 نقطة\n" +
                "💰 قيم البطاقات: 20 - 100\n" +
                "🔥 أعلى بطاقة: 100 نقطة\n" +
                "💀 الفريق الذي يصل إلى 0 يخسر\n\n" +

                "👥 الانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n" +
                "!مزاج انضم اصفر\n" +
                "!مزاج انضم بنفسجي\n\n" +

                "🔄 تغيير الفريق:\n" +
                "!مزاج تغيير <الفريق>\n\n" +

                "👥 عرض اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +

                "▶️ بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +

                "🎴 اختيار بطاقة:\n" +
                "اكتب الرقم مباشرة مثل:\n" +
                "13\n\n" +

                "أو:\n" +
                "!مزاج اختار 13\n\n" +

                "🃏 عرض البطاقات:\n" +
                "!مزاج بطاقات\n\n" +

                "🛑 إنهاء اللعبة:\n" +
                "!مزاج انهاء\n\n" +

                "⏱️ مدة الدور: 25 ثانية";

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
                    $"{team.Score} / 400\n";

                place++;
            }

            if (ranking.Count > 0)
            {
                Team winner =
                    ranking
                        .OrderByDescending(
                            x => x.Score)
                        .First();

                result +=
                    $"\n👑 الفائز: " +
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
            return value.Trim()
                .ToLowerInvariant() switch
            {
                "احمر" => "احمر",
                "الأحمر" => "احمر",
                "الاحمر" => "احمر",

                "ازرق" => "ازرق",
                "الأزرق" => "ازرق",
                "الازرق" => "ازرق",

                "اصفر" => "اصفر",
                "الأصفر" => "اصفر",
                "الاصفر" => "اصفر",

                "بنفسجي" => "بنفسجي",
                "البنفسجي" => "بنفسجي",

                _ => value.Trim()
            };
        }

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
        // جلب اسم اللاعب
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

    // =============================================================
    // Mazaj Game
    // =============================================================

    public class MazajGame
    {
        public const int StartingPoints = 400;

        public int PointsPerCard { get; }

        public int TeamCount { get; }

        public List<Team> Teams { get; }

        public List<Card> Cards { get; }

        public List<string> TurnOrder { get; }

        public bool Started { get; set; }

        public int CurrentPlayerIndex { get; set; }

        public int TurnVersion { get; set; }

        public string GroupId { get; set; } = "";

        public MazajGame(
            int pointsPerCard,
            int teamCount)
        {
            PointsPerCard =
                pointsPerCard;

            TeamCount =
                teamCount;

            TurnOrder =
                new List<string>();

            Cards =
                CreateCards();

            List<Team> allTeams =
                new List<Team>
                {
                    new Team("احمر", "🟥"),
                    new Team("ازرق", "🟦"),
                    new Team("اصفر", "🟨"),
                    new Team("بنفسجي", "🟪")
                };

            Teams =
                allTeams
                    .Take(teamCount)
                    .ToList();
        }

        public string CurrentPlayerId
        {
            get
            {
                if (TurnOrder.Count == 0)
                {
                    return "";
                }

                if (CurrentPlayerIndex < 0 ||
                    CurrentPlayerIndex >= TurnOrder.Count)
                {
                    return "";
                }

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

        public Team? GetTeamByPlayer(
            string userId)
        {
            return Teams.FirstOrDefault(
                x => x.Players.ContainsKey(userId)
            );
        }

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
        // إنشاء الـ65 بطاقة
        // =========================================================

        private static List<Card> CreateCards()
        {
            List<Card> cards =
                new List<Card>();

            List<(string Name, int Value)> specialCards =
                new List<(string, int)>
                {
                    // أعلى قيمة ثابتة
                    ("ضربة الوحش محمد 🇮🇶❤️", 100),

                    ("ضربة يوسف المهندس", 90),

                    ("ضربة سرمد الوحش 🔥", 85),

                    ("هولو وئام الفگر", 75),
                    ("طاحج حضج توت 😂", 70),
                    ("صخام بوجهك ايهاب", 65),
                    ("سراوي تيتي لاتحل ولا تربط", 60),
                    ("هذا حظ زوز", 55),
                    ("لولو التعبانه", 50),
                    ("نواره السلبيه", 45),
                    ("ضربة ابو عماد", 70),
                    ("ضربة حمدي الوزير", 65),
                    ("ضربة حيدر بنكه", 60),
                    ("ضربة جمو موسيقى", 55),
                    ("ضربة اساور صاروخ باليستي", 80),
                    ("صاروخ ارض ارض", 75),
                    ("ضربة علي القويه", 65),
                    ("ضربة ابو جنه", 60),
                    ("ضربة سند سوريا", 70),
                    ("ضربة مزاج", 50),
                    ("حظك اليوم", 40),
                    ("المفاجأة", 45),
                    ("ضربة الحظ", 50),
                    ("البطاقة الغامضة", 55),
                    ("ضربة قوية", 65),
                    ("ضربة خفيفة", 25),
                    ("الحظ العاثر", 35),
                    ("الحظ الجميل", 30),
                    ("مفاجأة مزاج", 45),
                    ("الضربة الأخيرة", 80),
                    ("ضربة البرق", 70),
                    ("ضربة النار", 75),
                    ("ضربة الصدمة", 65),
                    ("الضربة السرية", 60),
                    ("بطاقة الحظ", 40),
                    ("بطاقة النحس", 35)
                };

            int number = 1;

            foreach (var item in specialCards)
            {
                cards.Add(
                    new Card(
                        number,
                        item.Name,
                        item.Value
                    )
                );

                number++;
            }

            // إكمال العدد إلى 65 بطاقة
            Random random =
                new Random();

            while (cards.Count < 65)
            {
                int value =
                    random.Next(20, 81);

                cards.Add(
                    new Card(
                        number,
                        "بطاقة مزاج",
                        value
                    )
                );

                number++;
            }

            // نتأكد أن بطاقة محمد هي الأعلى والثابتة
            Card? mahmoodCard =
                cards.FirstOrDefault(
                    x => x.Name == "ضربة الوحش محمد 🇮🇶❤️"
                );

            if (mahmoodCard != null)
            {
                // هي أصلاً 100
            }

            return cards;
        }
    }

    // =============================================================
    // Team
    // =============================================================

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

            Score =
                MazajGame.StartingPoints;

            Players =
                new Dictionary<string, string>();
        }
    }

    // =============================================================
    // Card
    // =============================================================

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
