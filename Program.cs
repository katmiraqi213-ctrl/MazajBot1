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

            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    // منع معالجة الرسالة مرتين
                    if (!string.IsNullOrWhiteSpace(message.MessageId))
                    {
                        lock (_messageLock)
                        {
                            if (!_processedMessages.Add(message.MessageId))
                                return;

                            if (_processedMessages.Count > 5000)
                                _processedMessages.Clear();
                        }
                    }

                    string text =
                        message.Content?.Trim() ?? "";

                    // =================================================
                    // الرقم المباشر
                    // يعمل فقط أثناء اللعبة
                    // وصاحب الدور فقط يستطيع استخدامه
                    // =================================================

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

                        // قبل اللعبة: تجاهل الرقم بصمت
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
                        "COMMAND ERROR: " +
                        ex.Message
                    );
                }
            };

            bool loginResult =
                await _client.Login(
                    email,
                    password
                );

            if (!loginResult)
            {
                Console.WriteLine(
                    "❌ فشل تسجيل الدخول إلى Wolf."
                );

                return;
            }

            Console.WriteLine(
                "✅ تم تسجيل الدخول إلى Wolf."
            );

            await _client.Connect();

            // إبقاء البوت يعمل
            await Task.Delay(
                Timeout.Infinite
            );
        }

        // =============================================================
        // الأوامر
        // =============================================================

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
                        message
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
                            "❌ استخدم:\n" +
                            "!مزاج اختار <رقم>"
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

        // =============================================================
        // إنشاء لعبة
        // =============================================================

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
                "🟥 400 نقطة\n" +
                "🟦 400 نقطة"
            );
        }

        // =============================================================
        // الانضمام إلى الفريق
        // =============================================================

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
                    "❌ الفريق غير موجود.\n" +
                    "المتاح: احمر أو ازرق."
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

            // التأكد أن اللاعب غير موجود بفريق آخر
            foreach (Team existingTeam in _game.Teams)
            {
                if (existingTeam.Players.ContainsKey(userId))
                {
                    await client.Reply(
                        message,
                        $"⚠️ أنت منضم مسبقاً إلى " +
                        $"{existingTeam.Emoji} " +
                        $"{existingTeam.DisplayName}."
                    );

                    return;
                }
            }

            team.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم انضمامك إلى " +
                $"{team.Emoji} " +
                $"{team.DisplayName}\n" +
                $"👤 اللاعب: {nickname}"
            );
        }

        // =============================================================
        // تغيير الفريق
        // =============================================================

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
                    "!مزاج تغيير <احمر|ازرق>"
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
                        $"{newTeam.Emoji} " +
                        $"{newTeam.DisplayName}"
                    );

                    return;
                }
            }

            newTeam.Players[userId] =
                nickname;

            await client.Reply(
                message,
                $"✅ تم تسجيلك في " +
                $"{newTeam.Emoji} " +
                $"{newTeam.DisplayName}"
            );
        }

        // =============================================================
        // عرض اللاعبين
        // =============================================================

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
                    $"{team.Emoji} " +
                    $"{team.DisplayName}\n";

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

        // =============================================================
        // بدء اللعبة
        // =============================================================

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

            // كل فريق يجب أن يحتوي لاعباً واحداً على الأقل
            if (_game.Teams.Any(
                x => x.Players.Count == 0))
            {
                await client.Reply(
                    message,
                    "❌ يجب أن يكون في كل فريق " +
                    "لاعب واحد على الأقل."
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

            await client.Reply(
                message,
                "🎭🔥 بدأت لعبة مزاج!\n\n" +
                $"👥 عدد اللاعبين: " +
                $"{_game.TurnOrder.Count}"
            );

            await SendBoards(
                client,
                message,
                _game,
                _game.CurrentPlayerName
            );

            _ = StartTurnTimer(
                client,
                _game
            );
        }

        // =============================================================
        // اختيار البطاقة
        // =============================================================

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

            // ---------------------------------------------------------
            // التحقق من الرقم
            // ---------------------------------------------------------

            if (number < 1 || number > 65)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ رقم البطاقة يجب أن يكون " +
                        "من 1 إلى 65."
                    );
                }

                return;
            }

            string userId =
                message.UserId;

            // ---------------------------------------------------------
            // اللاعب غير مشارك
            // ---------------------------------------------------------

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

            // ---------------------------------------------------------
            // ليس دور اللاعب
            // ---------------------------------------------------------

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
                return;

            // ---------------------------------------------------------
            // البطاقة مستخدمة
            // ---------------------------------------------------------

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
                return;

            // ---------------------------------------------------------
            // تسجيل البطاقة
            // ---------------------------------------------------------

            card.Used = true;

            Team affectedTeam;

            // ---------------------------------------------------------
            // البطاقة الموجبة
            // تخصم من الخصم فقط
            // ---------------------------------------------------------

            if (card.Value > 0)
            {
                affectedTeam =
                    game.Teams.First(
                        x => x != playerTeam
                    );

                affectedTeam.Score =
                    Math.Max(
                        0,
                        affectedTeam.Score -
                        card.Value
                    );
            }
            else
            {
                // -----------------------------------------------------
                // البطاقة السالبة
                // تخصم من فريق اللاعب نفسه
                // -----------------------------------------------------

                affectedTeam =
                    playerTeam;

                int loss =
                    Math.Abs(card.Value);

                affectedTeam.Score =
                    Math.Max(
                        0,
                        affectedTeam.Score -
                        loss
                    );
            }

            // إلغاء المؤقت القديم
            game.TurnVersion++;

            // ---------------------------------------------------------
            // نتيجة البطاقة
            // ---------------------------------------------------------

            string result =
                BuildCardResult(
                    game,
                    playerTeam,
                    affectedTeam,
                    card
                );

            await client.Reply(
                message,
                result
            );

            // ---------------------------------------------------------
            // انتهاء اللعبة إذا وصل فريق إلى صفر
            // ---------------------------------------------------------

            if (affectedTeam.Score <= 0)
            {
                game.Started = false;

                game.TurnVersion++;

                Team winner =
                    game.Teams.First(
                        x => x != affectedTeam
                    );

                await client.Reply(
                    message,
                    "🏁 انتهت اللعبة!\n\n" +
                    $"💀 الخاسر: " +
                    $"{affectedTeam.Emoji} " +
                    $"{affectedTeam.DisplayName}\n" +
                    $"👑 الفائز: " +
                    $"{winner.Emoji} " +
                    $"{winner.DisplayName}\n\n" +
                    BuildFinalResults(game)
                );

                _game = null;

                return;
            }

            // ---------------------------------------------------------
            // انتهاء كل البطاقات
            // ---------------------------------------------------------

            if (game.AllCardsUsed)
            {
                game.Started = false;

                game.TurnVersion++;

                await client.Reply(
                    message,
                    "🏁 انتهت كل البطاقات!\n\n" +
                    BuildFinalResults(game)
                );

                _game = null;

                return;
            }

            // ---------------------------------------------------------
            // الدور التالي
            // ---------------------------------------------------------

            game.CurrentPlayerIndex++;

            if (game.CurrentPlayerIndex >=
                game.TurnOrder.Count)
            {
                game.CurrentPlayerIndex = 0;
            }

            string nextPlayer =
                game.CurrentPlayerName;

            // ---------------------------------------------------------
            // انتظار ثانيتين بعد النتيجة
            // ---------------------------------------------------------

            await Task.Delay(
                TimeSpan.FromSeconds(2)
            );

            if (_game != game ||
                !game.Started)
            {
                return;
            }

            // ---------------------------------------------------------
            // لوحة النتائج
            // ---------------------------------------------------------

            await client.Reply(
                message,
                BuildScoreBoard(game)
            );

            // ---------------------------------------------------------
            // لوحة الأرقام
            // ---------------------------------------------------------

            await client.Reply(
                message,
                BuildCardBoard(game)
            );

            // ---------------------------------------------------------
            // اللاعب التالي
            // ---------------------------------------------------------

            await client.Reply(
                message,
                $"👤 اللاعب التالي: " +
                $"{nextPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار رقم"
            );

            // ---------------------------------------------------------
            // مؤقت جديد
            // ---------------------------------------------------------

            _ = StartTurnTimer(
                client,
                game
            );
        }

        // =============================================================
        // نتيجة البطاقة
        // =============================================================

        private static string BuildCardResult(
            MazajGame game,
            Team playerTeam,
            Team affectedTeam,
            Card card)
        {
            string value =
                Math.Abs(card.Value).ToString();

            if (card.Value > 0)
            {
                return
                    $"🎴{card.Number} 🃏{card.Name}\n" +
                    $"{playerTeam.Emoji} " +
                    $"{playerTeam.DisplayName} جابوا {value} | " +
                    $"{affectedTeam.Emoji} " +
                    $"{affectedTeam.DisplayName} خسروا {value}\n" +
                    BuildCompactScores(game);
            }

            return
                $"🎴{card.Number} 🃏{card.Name}\n" +
                $"{playerTeam.Emoji} " +
                $"{playerTeam.DisplayName} خسر {value}\n" +
                BuildCompactScores(game);
        }

        // =============================================================
        // النقاط المختصرة
        // =============================================================

        private static string BuildCompactScores(
            MazajGame game)
        {
            Team red =
                game.Teams[0];

            Team blue =
                game.Teams[1];

            return
                $"💍 {red.Emoji}{red.Score} " +
                $"{blue.Emoji}{blue.Score}";
        }

        // =============================================================
        // مؤقت 25 ثانية
        // =============================================================

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
                    $"⏰ انتهى وقت اللاعب: " +
                    $"{oldPlayer}\n\n" +
                    "🚫 لم يتم اختيار أي بطاقة.";

                if (!string.IsNullOrWhiteSpace(
                    game.GroupId))
                {
                    await client.GroupMessage(
                        game.GroupId,
                        result
                    );

                    // لوحة النتائج
                    await client.GroupMessage(
                        game.GroupId,
                        BuildScoreBoard(game)
                    );

                    // لوحة الأرقام
                    await client.GroupMessage(
                        game.GroupId,
                        BuildCardBoard(game)
                    );

                    // اللاعب التالي
                    await client.GroupMessage(
                        game.GroupId,
                        $"👤 اللاعب التالي: " +
                        $"{nextPlayer}\n" +
                        "⏱️ عندك 25 ثانية تختار رقم"
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
                    "TURN TIMER ERROR: " +
                    ex.Message
                );
            }
        }

        // =============================================================
        // إرسال اللوحات
        // =============================================================

        private static async Task SendBoards(
            IWolfClient client,
            Message message,
            MazajGame game,
            string nextPlayer)
        {
            await client.Reply(
                message,
                BuildScoreBoard(game)
            );

            await client.Reply(
                message,
                BuildCardBoard(game)
            );

            if (!string.IsNullOrWhiteSpace(
                nextPlayer))
            {
                await client.Reply(
                    message,
                    $"👤 اللاعب التالي: " +
                    $"{nextPlayer}\n" +
                    "⏱️ عندك 25 ثانية تختار رقم"
                );
            }
        }

        // =============================================================
        // إنهاء اللعبة
        // =============================================================

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

            await client.Reply(
                message,
                "🛑 تم إنهاء لعبة مزاج."
            );

            await client.Reply(
                message,
                BuildFinalResults(game)
            );

            _game = null;
        }

        // =============================================================
        // لوحة النتائج
        // =============================================================

        private static string BuildScoreBoard(
            MazajGame game)
        {
            Team red =
                game.Teams[0];

            Team blue =
                game.Teams[1];

            return
                "   💍 مزاج\n" +
                "┌────────┐\n" +
                $"│ {red.Emoji} {red.Score,3} │\n" +
                $"│ {blue.Emoji} {blue.Score,3} │\n" +
                "└────────┘";
        }

        // =============================================================
        // لوحة الأرقام
        // 65 بطاقة
        // 7 أرقام بالسطر
        // =============================================================

        private static string BuildCardBoard(
            MazajGame game)
        {
            string[] colors =
            {
                "🟥",
                "🟦",
                "🟨",
                "🟪"
            };

            List<string> rows =
                new List<string>();

            List<string> current =
                new List<string>();

            for (int i = 1; i <= 65; i++)
            {
                Card card =
                    game.Cards[i - 1];

                string display =
                    card.Used
                        ? "❌"
                        : colors[(i - 1) % 4] + i;

                current.Add(display);

                if (current.Count == 7 ||
                    i == 65)
                {
                    rows.Add(
                        string.Join(
                            " ",
                            current
                        )
                    );

                    current.Clear();
                }
            }

            return
                "💍 مـزاج\n\n" +
                string.Join(
                    "\n",
                    rows
                );
        }

        // =============================================================
        // عرض البطاقات
        // =============================================================

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

        // =============================================================
        // المساعدة
        // =============================================================

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
                "!مزاج تغيير <الفريق>\n\n" +

                "👥 اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +

                "▶️ بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +

                "🎴 اختيار البطاقة:\n" +
                "اكتب الرقم مباشرة مثل: 13\n" +
                "أو !مزاج اختار 13\n\n" +

                "🃏 عرض البطاقات:\n" +
                "!مزاج بطاقات\n\n" +

                "🛑 إنهاء:\n" +
                "!مزاج انهاء\n\n" +

                "⏱️ مدة الدور: 25 ثانية\n" +
                "💰 البداية: 400 نقطة لكل فريق\n" +
                "🎴 65 بطاقة: 57 موجبة و8 سالبة";

            await client.Reply(
                message,
                help
            );
        }

        // =============================================================
        // النتائج النهائية
        // =============================================================

        private static string BuildFinalResults(
            MazajGame game)
        {
            string result =
                "🏆 النتائج النهائية\n\n";

            foreach (
                Team team in game.Teams.OrderByDescending(
                    x => x.Score))
            {
                result +=
                    $"{team.Emoji} " +
                    $"{team.DisplayName} — " +
                    $"{team.Score} نقطة\n";
            }

            Team winner =
                game.Teams
                    .OrderByDescending(
                        x => x.Score)
                    .First();

            result +=
                $"\n👑 الفائز: " +
                $"{winner.Emoji} " +
                $"{winner.DisplayName}";

            return result;
        }

        // =============================================================
        // أسماء الفرق
        // =============================================================

        private static string NormalizeTeam(
            string value)
        {
            return value.Trim()
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

        // =============================================================
        // قراءة الرقم
        // =============================================================

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

        // =============================================================
        // تنسيق النقاط
        // =============================================================

        private static string FormatValue(
            int value)
        {
            return value >= 0
                ? $"+{value}"
                : value.ToString();
        }

        // =============================================================
        // الحصول على اسم اللاعب
        // =============================================================

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
        public int PointsPerCard { get; } = 400;

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
            TurnOrder =
                new List<string>();

            Cards =
                CreateCards();

            Teams =
                new List<Team>
                {
                    new Team(
                        "احمر",
                        "🟥",
                        "الأمراء"
                    ),

                    new Team(
                        "ازرق",
                        "🟦",
                        "النجوم"
                    )
                };

            foreach (Team team in Teams)
            {
                team.Score = 400;
            }

            Started = false;

            CurrentPlayerIndex = 0;

            TurnVersion = 0;
        }

        // =============================================================
        // معرف اللاعب الحالي
        // =============================================================

        public string CurrentPlayerId
        {
            get
            {
                if (TurnOrder.Count == 0 ||
                    CurrentPlayerIndex < 0 ||
                    CurrentPlayerIndex >= TurnOrder.Count)
                {
                    return "";
                }

                return TurnOrder[
                    CurrentPlayerIndex
                ];
            }
        }

        // =============================================================
        // اسم اللاعب الحالي
        // =============================================================

        public string CurrentPlayerName
        {
            get
            {
                string playerId =
                    CurrentPlayerId;

                if (string.IsNullOrWhiteSpace(
                    playerId))
                {
                    return "لاعب غير معروف";
                }

                foreach (Team team in Teams)
                {
                    if (team.Players.TryGetValue(
                        playerId,
                        out string? nickname))
                    {
                        return nickname;
                    }
                }

                return playerId;
            }
        }

        // =============================================================
        // هل انتهت جميع البطاقات؟
        // =============================================================

        public bool AllCardsUsed
        {
            get
            {
                return Cards.All(
                    x => x.Used
                );
            }
        }

        // =============================================================
        // الحصول على فريق اللاعب
        // =============================================================

        public Team? GetTeamByPlayer(
            string userId)
        {
            foreach (Team team in Teams)
            {
                if (team.Players.ContainsKey(userId))
                {
                    return team;
                }
            }

            return null;
        }

        // =============================================================
        // إنشاء البطاقات
        // 57 موجبة + 8 سالبة = 65
        // =============================================================

        private static List<Card> CreateCards()
        {
            List<Card> cards =
                new List<Card>();

            // ---------------------------------------------------------
            // 57 بطاقة موجبة
            // ---------------------------------------------------------

            int[] positiveValues =
            {
                100,
                90,
                85,

                80,
                80,
                80,

                75,
                75,
                75,

                70,
                70,
                70,
                70,

                65,
                65,
                65,
                65,

                60,
                60,
                60,
                60,

                55,
                55,
                55,
                55,

                50,
                50,
                50,
                50,
                50,

                45,
                45,
                45,
                45,

                40,
                40,
                40,
                40,

                35,
                35,
                35,
                35,

                30,
                30,
                30,
                30,

                25,
                25,
                25,
                25,

                20,
                20,
                20,
                20,

                15,
                15,
                15,

                10,
                10,
                10
            };

            for (
                int i = 0;
                i < positiveValues.Length;
                i++)
            {
                string name;

                if (i == 0)
                {
                    name =
                        "ضربة الوحش محمد 🇮🇶❤️";
                }
                else if (i == 1)
                {
                    name =
                        "ضربة يوسف المهندس";
                }
                else if (i == 2)
                {
                    name =
                        "ضربة سرمد الوحش 🔥";
                }
                else
                {
                    name =
                        $"ضربة مزاج {i + 1}";
                }

                cards.Add(
                    new Card(
                        i + 1,
                        name,
                        positiveValues[i]
                    )
                );
            }

            // ---------------------------------------------------------
            // 8 بطاقات سالبة
            // ---------------------------------------------------------

            int[] negativeValues =
            {
                -10,
                -15,
                -20,
                -25,
                -30,
                -35,
                -40,
                -50
            };

            for (
                int i = 0;
                i < negativeValues.Length;
                i++)
            {
                int number =
                    positiveValues.Length +
                    i +
                    1;

                cards.Add(
                    new Card(
                        number,
                        $"بطاقة نحس {number}",
                        negativeValues[i]
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

        public string DisplayName { get; }

        public int Score { get; set; }

        public Dictionary<string, string> Players { get; }

        public Team(
            string name,
            string emoji,
            string displayName)
        {
            Name = name;

            Emoji = emoji;

            DisplayName = displayName;

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
