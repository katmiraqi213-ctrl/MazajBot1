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
            string email = Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";
            string password = Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("❌ WOLF_EMAIL أو WOLF_PASSWORD غير موجود.");
                return;
            }

            Console.WriteLine("🚀 تشغيل Mazaj Bot...");

            _client = new WolfClient();

            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    // منع معالجة نفس الرسالة مرتين
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

                    string text = message.Content?.Trim() ?? "";

                    // الرقم المباشر يعمل فقط أثناء اللعبة
                    // وللاعب صاحب الدور فقط
                    if (TryParseNumber(text, out int directNumber))
                    {
                        if (_game != null && _game.Started)
                            await ChooseCard(client, message, directNumber, true);

                        return;
                    }

                    // أوامر مزاج
                    if (!text.StartsWith("!مزاج", StringComparison.OrdinalIgnoreCase))
                        return;

                    string command = text.Length > 5
                        ? text.Substring(5).Trim()
                        : "";

                    await HandleCommand(client, message, command);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("COMMAND ERROR: " + ex.Message);
                }
            };

            bool loginResult = await _client.Login(email, password);

            if (!loginResult)
            {
                Console.WriteLine("❌ فشل تسجيل الدخول إلى Wolf.");
                return;
            }

            Console.WriteLine("✅ تم تسجيل الدخول إلى Wolf.");

            await _client.Connect();

            // إبقاء البوت يعمل
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
            string[] parts = command.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            );

            if (parts.Length == 0)
            {
                await SendHelp(client, message);
                return;
            }

            string action = parts[0].ToLowerInvariant();

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
        // إنشاء لعبة
        // فريقان فقط
        // 400 نقطة لكل فريق
        // 65 بطاقة
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

            _game.GroupId = message.GroupId ?? "";

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج!\n\n" +
                "🟥 الفريق الأحمر: 400 نقطة\n" +
                "🟦 الفريق الأزرق: 400 نقطة\n" +
                "🎴 البطاقات: 65\n" +
                "➕ 57 بطاقة موجبة\n" +
                "➖ 8 بطاقات سالبة\n\n" +
                "📌 للانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n\n" +
                "📌 وبعد اكتمال اللاعبين:\n" +
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
                    "!مزاج انضم <احمر|ازرق>"
                );

                return;
            }

            string teamName = NormalizeTeam(parts[1]);

            Team? team = _game.Teams.FirstOrDefault(
                x => x.Name == teamName
            );

            if (team == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود. المتاح: احمر أو ازرق."
                );

                return;
            }

            string userId = message.UserId;

            string nickname =
                await GetNickname(client, userId);

            foreach (Team existingTeam in _game.Teams)
            {
                if (existingTeam.Players.ContainsKey(userId))
                {
                    await client.Reply(
                        message,
                        $"⚠️ أنت منضم مسبقاً إلى " +
                        $"{existingTeam.Emoji} " +
                        $"{existingTeam.Name}."
                    );

                    return;
                }
            }

            team.Players[userId] = nickname;

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

            string userId = message.UserId;

            string nickname =
                await GetNickname(client, userId);

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

            // اللاعب غير مشارك
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
                // الرقم المباشر يتم تجاهله بصمت
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

            // البطاقة مستخدمة
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

            card.Used = true;

            Team affectedTeam;

            string scoreMessage;

            if (card.Value > 0)
            {
                // الموجب يخصم من الخصم
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

                scoreMessage =
                    $"🎯 {playerTeam.Emoji} " +
                    $"اختار البطاقة وربح " +
                    $"{card.Value} على الفريق الخصم\n" +
                    $"💥 {affectedTeam.Emoji} " +
                    $"خسر {card.Value} نقطة";
            }
            else
            {
                // السالب يخصم من صاحب الدور
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

                scoreMessage =
                    $"💥 {playerTeam.Emoji} " +
                    $"خسر {loss} نقطة " +
                    "بسبب البطاقة السالبة";
            }

            game.TurnVersion++;

            await client.Reply(
                message,
                "/me\n\n" +
                $"🎴 تم اختيار البطاقة رقم " +
                $"{card.Number}\n" +
                $"🃏 {card.Name}\n" +
                $"💰 القيمة: " +
                $"{FormatValue(card.Value)}\n\n" +
                scoreMessage
            );

            // فوز/خسارة فريق
            if (affectedTeam.Score <= 0)
            {
                game.Started = false;
                game.TurnVersion++;

                await SendBoards(
                    client,
                    message,
                    game,
                    ""
                );

                Team winner =
                    game.Teams.First(
                        x => x != affectedTeam
                    );

                await client.Reply(
                    message,
                    $"🏁 انتهت اللعبة!\n\n" +
                    $"💀 الفريق الخاسر: " +
                    $"{affectedTeam.Emoji} " +
                    $"{affectedTeam.Name}\n" +
                    $"👑 الفائز: " +
                    $"{winner.Emoji} " +
                    $"{winner.Name}\n\n" +
                    BuildFinalResults(game)
                );

                _game = null;

                return;
            }

            // انتهت كل البطاقات
            if (game.AllCardsUsed)
            {
                game.Started = false;
                game.TurnVersion++;

                await SendBoards(
                    client,
                    message,
                    game,
                    ""
                );

                await client.Reply(
                    message,
                    "🏁 انتهت كل البطاقات!\n\n" +
                    BuildFinalResults(game)
                );

                _game = null;

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

            await SendBoards(
                client,
                message,
                game,
                nextPlayer
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

                    await SendBoardsToGroup(
                        client,
                        game,
                        nextPlayer
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

        // =========================================================
        // إرسال اللوحتين بشكل منفصل
        // =========================================================

        private static async Task SendBoards(
            IWolfClient client,
            Message message,
            MazajGame game,
            string nextPlayer)
        {
            // لوحة النتائج منفصلة
            await client.Reply(
                message,
                BuildScoreBoard(game)
            );

            // لوحة الأرقام منفصلة
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

        private static async Task SendBoardsToGroup(
            IWolfClient client,
            MazajGame game,
            string nextPlayer)
        {
            await client.GroupMessage(
                game.GroupId,
                BuildScoreBoard(game)
            );

            await client.GroupMessage(
                game.GroupId,
                BuildCardBoard(game)
            );

            await client.GroupMessage(
                game.GroupId,
                $"👤 اللاعب التالي: " +
                $"{nextPlayer}\n" +
                "⏱️ عندك 25 ثانية تختار رقم"
            );
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

            MazajGame game = _game;

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

        // =========================================================
        // لوحة النتائج الصغيرة
        // منفصلة عن لوحة الأرقام
        // =========================================================

        private static string BuildScoreBoard(
            MazajGame game)
        {
            Team red = game.Teams[0];
            Team blue = game.Teams[1];

            return
                "   💍 مزاج\n" +
                "┌────────┐\n" +
                $"│ {red.Emoji} {red.Score,3} │\n" +
                $"│ {blue.Emoji} {blue.Score,3} │\n" +
                "└────────┘";
        }

        // =========================================================
        // لوحة الأرقام
        // 7 أرقام بكل سطر
        // 65 بطاقة
        // =========================================================

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

            List<string> rows = new();
            List<string> current = new();

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

            foreach (Card card
                in _game.Cards)
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

        // =========================================================
        // النتائج النهائية
        // =========================================================

        private static string BuildFinalResults(
            MazajGame game)
        {
            string result =
                "🏆 النتائج النهائية\n\n";

            foreach (
                Team team
                in game.Teams.OrderByDescending(
                    x => x.Score))
            {
                result +=
                    $"{team.Emoji} " +
                    $"{team.Name} — " +
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
                $"{winner.Name}";

            return result;
        }

        // =========================================================
        // أسماء الفرق
        // =========================================================

        private static string NormalizeTeam(
            string value)
        {
            return value.Trim()
                .ToLowerInvariant()
                switch
                {
                    "احمر" => "احمر",
                    "الأحمر" => "احمر",

                    "ازرق" => "ازرق",
                    "الأزرق" => "ازرق",

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
        // تنسيق النقاط
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
                // تجاهل الخطأ والعودة إلى ID
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
                        "🟥"
                    ),

                    new Team(
                        "ازرق",
                        "🟦"
                    )
                };

            foreach (Team team in Teams)
            {
                team.Score = 400;
            }
        }

        // =========================================================
        // اللاعب الحالي
        // =========================================================

        public string CurrentPlayerId
        {
            get
            {
                if (
                    TurnOrder.Count == 0 ||
                    CurrentPlayerIndex < 0 ||
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

        public string CurrentPlayerName
        {
            get
            {
                string userId =
                    CurrentPlayerId;

                if (
                    string.IsNullOrWhiteSpace(
                        userId))
                {
                    return "";
                }

                foreach (Team team in Teams)
                {
                    if (
                        team.Players.TryGetValue(
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
                    userId
                )
            );
        }

        // =========================================================
        // هل انتهت البطاقات؟
        // =========================================================

        public bool AllCardsUsed =>
            Cards.All(x => x.Used);

        // =========================================================
        // إنشاء البطاقات
        // 57 موجبة + 8 سالبة = 65
        // =========================================================

        private static List<Card> CreateCards()
        {
            List<(string Name, int Value)> data =
                new()
                {
                    ("ضربة الوحش محمد 🇮🇶❤️", 100),
                    ("ضربة يوسف المهندس", 90),
                    ("ضربة سرمد الوحش 🔥", 85),

                    ("هولو وئام الفگر", 80),
                    ("طاحج حضج توت 😂", 80),
                    ("صخام بوجهك ايهاب", 75),
                    ("سراوي تيتي لاتحل ولا تربط", 75),
                    ("هذا حظ زوز", 70),
                    ("لولو التعبانه", 70),
                    ("نواره السلبيه", 65),
                    ("ضربة ابو عماد", 65),
                    ("ضربة حمدي الوزير", 60),
                    ("ضربة حيدر بنكه", 60),
                    ("ضربة جمو موسيقى", 55),
                    ("ضربة اساور صاروخ باليستي", 55),
                    ("صاروخ ارض ارض", 50),
                    ("ضربة علي القويه", 50),
                    ("ضربة ابو جنه", 45),
                    ("ضربة سند سوريا", 45),
                    ("ضربة مزاج", 40),
                    ("حظك اليوم", 40),
                    ("المفاجأة", 35),
                    ("ضربة الحظ", 35),
                    ("البطاقة الغامضة", 30),
                    ("ضربة قوية", 30),
                    ("ضربة خفيفة", 25),
                    ("الحظ العاثر", 25),
                    ("الحظ الجميل", 20),
                    ("مفاجأة مزاج", 20),
                    ("الضربة الأخيرة", 15),
                    ("ضربة البرق", 15),
                    ("ضربة النار", 10),
                    ("ضربة الصدمة", 10),
                    ("الضربة السرية", 5),
                    ("بطاقة الحظ", 5),

                    ("بطاقة مزاج 36", 10),
                    ("بطاقة مزاج 37", 15),
                    ("بطاقة مزاج 38", 20),
                    ("بطاقة مزاج 39", 25),
                    ("بطاقة مزاج 40", 30),
                    ("بطاقة مزاج 41", 35),
                    ("بطاقة مزاج 42", 40),
                    ("بطاقة مزاج 43", 45),
                    ("بطاقة مزاج 44", 50),
                    ("بطاقة مزاج 45", 55),
                    ("بطاقة مزاج 46", 60),
                    ("بطاقة مزاج 47", 65),
                    ("بطاقة مزاج 48", 70),
                    ("بطاقة مزاج 49", 75),
                    ("بطاقة مزاج 50", 80),
                    ("بطاقة مزاج 51", 85),
                    ("بطاقة مزاج 52", 90),
                    ("بطاقة مزاج 53", 95),
                    ("بطاقة مزاج 54", 100),
                    ("بطاقة مزاج 55", 40),
                    ("بطاقة مزاج 56", 50),
                    ("بطاقة مزاج 57", 60),

                    // 8 بطاقات سالبة
                    ("بطاقة نحس 60", -20),
                    ("بطاقة نحس 61", -25),
                    ("بطاقة نحس 62", -30),
                    ("بطاقة نحس 63", -35),
                    ("بطاقة نحس 64", -40),
                    ("بطاقة نحس 65", -45),
                    ("بطاقة النحس الكبرى", -50),
                    ("بطاقة النحس الأخيرة", -60)
                };

            // التأكد من العدد
            if (data.Count != 65)
            {
                throw new InvalidOperationException(
                    "يجب أن تكون البطاقات 65 بالضبط."
                );
            }

            // التأكد من 57 موجبة و8 سالبة
            if (
                data.Count(x => x.Value > 0) != 57 ||
                data.Count(x => x.Value < 0) != 8)
            {
                throw new InvalidOperationException(
                    "يجب أن تكون البطاقات 57 موجبة و8 سالبة."
                );
            }

            // لا توجد بطاقة فوق +100
            if (data.Any(x => x.Value > 100))
            {
                throw new InvalidOperationException(
                    "لا توجد بطاقة موجبة أكبر من 100."
                );
            }

            return data
                .Select(
                    (x, index) =>
                        new Card(
                            index + 1,
                            x.Name,
                            x.Value
                        )
                )
                .ToList();
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
