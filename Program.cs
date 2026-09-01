

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

        // منع الرسائل المكررة
        private static readonly HashSet<string> _processedMessages =
            new HashSet<string>();

        private static readonly object _messageLock =
            new object();

        private static readonly Dictionary<string, DateTime> _recentMessages =
            new Dictionary<string, DateTime>();

        // =========================================================
        // تشغيل البوت
        // =========================================================

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

            // =====================================================
            // استقبال الرسائل
            // =====================================================

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

                    // =================================================
                    // الرقم المباشر
                    // =================================================

                    if (TryParseNumber(text, out int directNumber))
                    {
                        if (_game != null &&
                            _game.Started)
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

                    // =================================================
                    // أوامر مزاج
                    // =================================================

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

            // =====================================================
            // تسجيل الدخول
            // =====================================================

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

            // =====================================================
            // الاتصال
            // =====================================================

            await _client.Connect();

            Console.WriteLine(
                "✅ تم الاتصال بـ Wolf."
            );

            // =====================================================
            // تهيئة الرسائل
            // =====================================================

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

        // =========================================================
        // منع التكرار
        // =========================================================

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
                await SendHelp(
                    client,
                    message
                );

                return;
            }

            string action =
                parts[0].ToLowerInvariant();

            switch (action)
            {
                case "جديد":

                    await NewGame(
                        client,
                        message,
                        parts
                    );

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
                            "❌ استخدم:\n!مزاج اختار <رقم>"
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
        // إنشاء اللعبة
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

            if (parts.Length < 3 ||
                !int.TryParse(
                    parts[1],
                    out int points) ||
                !int.TryParse(
                    parts[2],
                    out int teamCount))
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام الصحيح:\n" +
                    "!مزاج جديد <قيمة البطاقة> <عدد الفرق>\n\n" +
                    "مثال:\n" +
                    "!مزاج جديد 20 2"
                );

                return;
            }

            if (points <= 0)
            {
                await client.Reply(
                    message,
                    "❌ يجب أن تكون قيمة البطاقة أكبر من صفر."
                );

                return;
            }

            if (teamCount < 2 ||
                teamCount > 4)
            {
                await client.Reply(
                    message,
                    "❌ عدد الفرق يجب أن يكون من 2 إلى 4."
                );

                return;
            }

            _game =
                new MazajGame(
                    points,
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

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج بنجاح!\n\n" +
                "❤️ رصيد كل فريق: 400 نقطة\n" +
                $"💰 قيمة البطاقات: حتى {points} نقطة\n" +
                $"👥 عدد الفرق: {teamCount}\n" +
                "🎴 عدد البطاقات: 65\n\n" +
                "📌 للانضمام:\n" +
                "!مزاج انضم <احمر|ازرق|اصفر|بنفسجي>\n\n" +
                "📌 بعد اكتمال اللاعبين:\n" +
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
                if (existingTeam.Players.ContainsKey(
                        userId))
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

            _game.TurnOrder.Clear();

            foreach (Team team in _game.Teams)
            {
                foreach (string userId
                    in team.Players.Keys)
                {
                    _game.TurnOrder.Add(
                        userId
                    );
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

            string result =
                "🎭🔥 لعبة مزاج بدأت ⚡\n\n" +
                $"👥 مجموع اللاعبين: {_game.TurnOrder.Count}\n\n" +
                BuildScoreBoard(_game) +
                "\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(_game) +
                "\n\n" +
                $"👤 اللاعب التالي: {firstPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

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

            if (number < 1 ||
                number > 65)
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
            if (!game.TurnOrder.Contains(
                    userId))
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
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        $"⏳ ليس دورك.\n" +
                        $"👤 الدور حالياً: " +
                        $"{game.CurrentPlayerName}"
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
                game.GetTeamByPlayer(
                    userId
                );

            if (playerTeam == null)
            {
                return;
            }

            // تسجيل البطاقة
            card.Used = true;

            // خصم النقاط
            playerTeam.Score -=
                card.Value;

            // إلغاء مؤقت الدور الحالي
            game.TurnVersion++;

            // =====================================================
            // تحديد الخسارة
            // =====================================================

            bool teamLost =
                playerTeam.Score <= 0;

            // منع ظهور رقم سالب
            if (playerTeam.Score < 0)
            {
                playerTeam.Score = 0;
            }

            // =====================================================
            // الرسالة الأولى: النتيجة فقط
            // =====================================================

            string result =
                "/me\n\n" +
                $"🎴 تم اختيار البطاقة رقم {card.Number}\n\n" +
                $"🃏 البطاقة: {card.Name}\n" +
                $"💰 القيمة: -{card.Value}\n\n" +
                $"{playerTeam.Emoji} الفريق {playerTeam.Name} " +
                $"خسر {card.Value} نقطة\n\n" +
                BuildScoreBoard(game);

            // =====================================================
            // الفريق خسر ووصل صفر
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

                // إرسال النتيجة أولاً
                await client.Reply(
                    message,
                    result
                );

                return;
            }

            // =====================================================
            // إرسال النتيجة وحدها
            // =====================================================

            await client.Reply(
                message,
                result
            );

            // =====================================================
            // انتظار ثانيتين
            // =====================================================

            await Task.Delay(
                TimeSpan.FromSeconds(2)
            );

            // =====================================================
            // التأكد أن اللعبة ما زالت نفسها
            // =====================================================

            if (_game != game ||
                !game.Started)
            {
                return;
            }

            // =====================================================
            // فحص البطاقات
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

            // =====================================================
            // الانتقال للاعب التالي
            // =====================================================

            game.CurrentPlayerIndex++;

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            // =====================================================
            // الرسالة الثانية: لوحة الأرقام فقط
            // =====================================================

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

            // =====================================================
            // مؤقت اللاعب الجديد
            // =====================================================

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

                // النتيجة الخاصة بانتهاء الوقت
                string result =
                    $"⏰ انتهى وقت اللاعب: {oldPlayer}\n\n" +
                    "🚫 لم يتم اختيار أي بطاقة.\n\n" +
                    BuildScoreBoard(game);

                await client.GroupMessage(
                    game.GroupId,
                    result
                );

                // انتظار بسيط حتى تنفصل النتيجة عن اللوحة
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
        // لوحة النتائج
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
                        : i.ToString()
                            .PadLeft(2, ' ');

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
                "!مزاج جديد <قيمة البطاقة> <عدد الفرق>\n" +
                "مثال: !مزاج جديد 20 2\n\n" +

                "❤️ رصيد كل فريق: 400 نقطة\n" +
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
        // اسم اللاعب
        // =========================================================

        private static async Task<string> GetNickname(
            IWolfClient client,
            string userId)
        {
            try
            {
                var user =
                    await client.GetUser(
                        userId
                    );

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
                CreateCards(
                    pointsPerCard
                );

            List<Team> allTeams =
                new List<Team>
                {
                    new Team(
                        "احمر",
                        "🟥"
                    ),

                    new Team(
                        "ازرق",
                        "🟦"
                    ),

                    new Team(
                        "اصفر",
                        "🟨"
                    ),

                    new Team(
                        "بنفسجي",
                        "🟪"
                    )
                };

            Teams =
                allTeams
                    .Take(teamCount)
                    .ToList();
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

                return TurnOrder[
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
        // جميع البطاقات
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
        // إنشاء البطاقات
        // =========================================================

        private static List<Card> CreateCards(
            int maxPoints)
        {
            List<string> names =
                new List<string>
                {
                    "ضربة الوحش محمد 🇮🇶❤️",
                    "هولو وئام الفگر",
                    "طاحج حضج توت 😂",
                    "صخام بوجهك ايهاب",
                    "سراوي تيتي لاتحل ولا تربط",
                    "هذا حظ زوز",
                    "لولو التعبانه",
                    "نواره السلبيه",
                    "ضربة ابو عماد",
                    "ضربة حمدي الوزير",
                    "ضربة حيدر بنكه",
                    "ضربة جمو موسيقى",
                    "ضربة اساور صاروخ باليستي",
                    "صاروخ ارض ارض",
                    "ضربة علي القويه",
                    "ضربة ابو جنه",
                    "ضربة سند سوريا",
                    "ضربة مزاج",
                    "حظك اليوم",
                    "المفاجأة",
                    "ضربة الحظ",
                    "البطاقة الغامضة",
                    "ضربة قوية",
                    "ضربة خفيفة",
                    "الحظ العاثر",
                    "الحظ الجميل",
                    "مفاجأة مزاج",
                    "الضربة الأخيرة",
                    "ضربة البرق",
                    "ضربة النار",
                    "ضربة الصدمة",
                    "الضربة السرية",
                    "بطاقة الحظ",
                    "بطاقة النحس",
                    "مفاجأة الفريق",
                    "الضربة الكبرى"
                };

            while (names.Count < 65)
            {
                names.Add(
                    "بطاقة مزاج"
                );
            }

            if (names.Count > 65)
            {
                names =
                    names
                        .Take(65)
                        .ToList();
            }

            List<Card> cards =
                new List<Card>();

            Random random =
                new Random();

            for (int i = 0;
                 i < names.Count;
                 i++)
            {
                int value;

                // قيمة البطاقة تكون خسارة فقط
                if (maxPoints <= 1)
                {
                    value = 1;
                }
                else
                {
                    value =
                        random.Next(
                            1,
                            maxPoints + 1
                        );
                }

                cards.Add(
                    new Card(
                        i + 1,
                        names[i],
                        value
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

        // يبدأ من 400
        public int Score { get; set; }

        public Dictionary<string, string> Players { get; }

        public Team(
            string name,
            string emoji)
        {
            Name =
                name;

            Emoji =
                emoji;

            Score =
                MazajGame.StartingPoints;

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

        // مقدار الخسارة
        public int Value { get; }

        public bool Used { get; set; }

        public Card(
            int number,
            string name,
            int value)
        {
            Number =
                number;

            Name =
                name;

            Value =
                value;

            Used =
                false;
        }
    }
}
