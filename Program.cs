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

        public static async Task Main(string[] args)
        {
            string email = Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";
            string password = Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
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
                    string text = message.Content?.Trim() ?? "";

                    // الرقم المباشر
                    if (TryParseNumber(text, out int number))
                    {
                        if (_game != null && _game.Started)
                        {
                            await ChooseCard(client, message, number, true);
                        }

                        return;
                    }

                    if (!text.StartsWith("!مزاج",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

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

            await _client.Connect();

            await Task.Delay(Timeout.Infinite);
        }

        private static async Task HandleCommand(
            IWolfClient client,
            Message message,
            string command)
        {
            string[] parts = command.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                await SendHelp(client, message);
                return;
            }

            string action = parts[0].ToLowerInvariant();

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
                            false);
                    }
                    else
                    {
                        await client.Reply(
                            message,
                            "❌ استخدم:\n!مزاج اختار <رقم>");
                    }
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
                        "❌ أمر غير معروف.\nاكتب !مزاج مساعدة");
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
                    "⚠️ توجد لعبة حالياً.\nاستخدم !مزاج انهاء أولاً.");
                return;
            }

            if (parts.Length != 1)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام الصحيح: !مزاج جديد");
                return;
            }

            _game = new MazajGame(400, 2);
            _game.GroupId = message.GroupId ?? "";

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج!\n\n" +
                "🟥 400 نقطة\n" +
                "🟦 400 نقطة");
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
                    "❌ لا توجد لعبة حالياً.");
                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ اللعبة بدأت بالفعل.");
                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام:\n" +
                    "!مزاج انضم <احمر|ازرق>");
                return;
            }

            string teamName = NormalizeTeam(parts[1]);

            Team? team = _game.Teams.FirstOrDefault(
                x => x.Name == teamName);

            if (team == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود.");
                return;
            }

            string userId = message.UserId;
            string nickname = await GetNickname(client, userId);

            foreach (Team existingTeam in _game.Teams)
            {
                if (existingTeam.Players.ContainsKey(userId))
                {
                    await client.Reply(
                        message,
                        $"⚠️ أنت منضم مسبقاً إلى {existingTeam.Emoji} {existingTeam.Name}.");
                    return;
                }
            }

            team.Players[userId] = nickname;

            await client.Reply(
                message,
                $"✅ تم انضمامك إلى {team.Emoji} {team.Name}\n" +
                $"👤 اللاعب: {nickname}");
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
                    "❌ لا توجد لعبة حالياً.");
                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ لا يمكن تغيير الفريق بعد بدء اللعبة.");
                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام:\n" +
                    "!مزاج تغيير <احمر|ازرق>");
                return;
            }

            string userId = message.UserId;
            string nickname = await GetNickname(client, userId);
            string newTeamName = NormalizeTeam(parts[1]);

            Team? newTeam = _game.Teams.FirstOrDefault(
                x => x.Name == newTeamName);

            if (newTeam == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود.");
                return;
            }

            foreach (Team team in _game.Teams)
            {
                if (team.Players.Remove(userId))
                {
                    newTeam.Players[userId] = nickname;

                    await client.Reply(
                        message,
                        $"🔄 تم تغيير فريقك إلى {newTeam.Emoji} {newTeam.Name}");

                    return;
                }
            }

            newTeam.Players[userId] = nickname;

            await client.Reply(
                message,
                $"✅ تم تسجيلك في {newTeam.Emoji} {newTeam.Name}");
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
                    "❌ لا توجد لعبة.");
                return;
            }

            string result = "👥 لاعبو لعبة مزاج\n\n";

            foreach (Team team in _game.Teams)
            {
                result +=
                    $"{team.Emoji} {team.Name} ({team.Players.Count} لاعبين)\n";

                if (team.Players.Count == 0)
                {
                    result += "لا يوجد لاعبين\n";
                }
                else
                {
                    int index = 1;

                    foreach (string name in team.Players.Values)
                    {
                        result += $"{index}. {name}\n";
                        index++;
                    }
                }

                result += "\n";
            }

            await client.Reply(
                message,
                result.TrimEnd());
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
                    "❌ لا توجد لعبة.");
                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "⚠️ اللعبة بدأت بالفعل.");
                return;
            }

            if (_game.Teams.Any(x => x.Players.Count == 0))
            {
                await client.Reply(
                    message,
                    "❌ يجب أن يكون في كل فريق لاعب واحد على الأقل.");
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

            _game.Started = true;
            _game.CurrentPlayerIndex = 0;
            _game.TurnVersion++;

            string firstPlayer = _game.CurrentPlayerName;

            string result =
                "🎭🔥 لعبة مزاج بدأت!\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(_game) +
                "\n\n" +
                $"🎯 الدور: {firstPlayer}  ⏱️ 25ث";

            await client.Reply(
                message,
                result);

            _ = StartTurnTimer(client, _game);
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
            if (_game == null || !_game.Started)
                return;

            MazajGame game = _game;

            if (number < 1 || number > 65)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ رقم البطاقة يجب أن يكون من 1 إلى 65.");
                }

                return;
            }

            string userId = message.UserId;

            if (!game.TurnOrder.Contains(userId))
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ أنت لست مشاركاً في اللعبة.");
                }

                return;
            }

            if (game.CurrentPlayerId != userId)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        $"⏳ ليس دورك.\nالدور حالياً: {game.CurrentPlayerName}");
                }

                return;
            }

            Card? card = game.Cards.FirstOrDefault(
                x => x.Number == number);

            if (card == null)
                return;

            if (card.Used)
            {
                if (!directNumber)
                {
                    await client.Reply(
                        message,
                        "❌ هذه البطاقة تم اختيارها مسبقاً.");
                }

                return;
            }

            Team? playerTeam = game.GetTeamByPlayer(userId);

            if (playerTeam == null)
                return;

            card.Used = true;
            game.TurnVersion++;

            string scoreMessage;

            if (card.Value > 0)
            {
                Team? opponentTeam = game.Teams.FirstOrDefault(
                    x => x != playerTeam);

                if (opponentTeam != null)
                {
                    opponentTeam.Score -= card.Value;

                    scoreMessage =
                        $"{playerTeam.Name} جاب {card.Value} | " +
                        $"{opponentTeam.Name} خسر {card.Value}";
                }
                else
                {
                    scoreMessage =
                        $"{playerTeam.Name} جاب {card.Value}";
                }
            }
            else
            {
                int loss = Math.Abs(card.Value);

                playerTeam.Score -= loss;

                scoreMessage =
                    $"{playerTeam.Name} خسر {loss}";
            }

            bool finished = game.AllCardsUsed;

            string result =
                $"🎴 تم اختيار البطاقة رقم {card.Number}\n" +
                $"🃏 {card.Name}\n" +
                $"💰 القيمة: {FormatValue(card.Value)}\n" +
                $"{scoreMessage}\n\n" +
                "🎴 لوحة الأرقام\n" +
                BuildCardBoard(game);

            if (finished)
            {
                game.Started = false;
                game.TurnVersion++;

                result +=
                    "\n\n🏁 انتهت اللعبة!\n\n" +
                    BuildFinalResults(game);

                _game = null;

                await client.Reply(
                    message,
                    result);

                return;
            }

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
                $"🎯 الدور: {nextPlayer}  ⏱️ 25ث";

            await client.Reply(
                message,
                result);

            _ = StartTurnTimer(client, game);
        }

        // =========================================================
        // مؤقت 25 ثانية
        // =========================================================

        private static async Task StartTurnTimer(
            IWolfClient client,
            MazajGame game)
        {
            int version = game.TurnVersion;

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(25));

                if (_game != game ||
                    !game.Started ||
                    game.TurnVersion != version)
                {
                    return;
                }

                string oldPlayerId =
                    game.CurrentPlayerId;

                string oldPlayer =
                    game.CurrentPlayerName;

                foreach (Team team in game.Teams)
                {
                    team.Players.Remove(oldPlayerId);
                }

                game.TurnOrder.Remove(oldPlayerId);
                game.TurnVersion++;

                bool teamEmpty =
                    game.Teams.Any(
                        x => x.Players.Count == 0);

                if (game.TurnOrder.Count == 0 ||
                    teamEmpty)
                {
                    game.Started = false;

                    string final =
                        $"⏰ انتهى وقت {oldPlayer}.\n" +
                        "🚫 تم إخراجه من اللعبة.\n\n" +
                        "🏁 انتهت لعبة مزاج!\n\n" +
                        BuildFinalResults(game);

                    _game = null;

                    if (!string.IsNullOrWhiteSpace(
                        game.GroupId))
                    {
                        await client.GroupMessage(
                            game.GroupId,
                            final);
                    }

                    return;
                }

                if (game.CurrentPlayerIndex >=
                    game.TurnOrder.Count)
                {
                    game.CurrentPlayerIndex = 0;
                }

                string nextPlayer =
                    game.CurrentPlayerName;

                string result =
                    $"⏰ انتهى وقت {oldPlayer}.\n" +
                    "🚫 تم إخراجه من اللعبة.\n\n" +
                    "🎴 لوحة الأرقام\n" +
                    BuildCardBoard(game) +
                    "\n\n" +
                    $"🎯 الدور: {nextPlayer}  ⏱️ 25ث";

                if (!string.IsNullOrWhiteSpace(
                    game.GroupId))
                {
                    await client.GroupMessage(
                        game.GroupId,
                        result);
                }

                _ = StartTurnTimer(client, game);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "TURN TIMER ERROR: " +
                    ex.Message);
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
                    "❌ لا توجد لعبة حالياً.");
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
                result);
        }

        // =========================================================
        // لوحة الأرقام
        // =========================================================

        private static string BuildCardBoard(
            MazajGame game)
        {
            List<string> rows =
                new List<string>();

            for (int start = 0;
                 start < 65;
                 start += 8)
            {
                List<string> cells =
                    new List<string>();

                for (int i = start + 1;
                     i <= Math.Min(start + 8, 65);
                     i++)
                {
                    Card card = game.Cards[i - 1];

                    cells.Add(
                        card.Used
                            ? "❌"
                            : i.ToString());
                }

                rows.Add(
                    string.Join(" ", cells));
            }

            return string.Join(
                "\n",
                rows);
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

                "🔄 تغيير الفريق قبل البدء:\n" +
                "!مزاج تغيير <احمر|ازرق>\n\n" +

                "👥 اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +

                "▶️ بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +

                "🎴 اختيار البطاقة:\n" +
                "اكتب الرقم مباشرة، مثل 13\n" +
                "أو !مزاج اختار 13\n\n" +

                "🛑 إنهاء اللعبة:\n" +
                "!مزاج انهاء\n\n" +

                "⏱️ مدة الدور: 25 ثانية";

            await client.Reply(
                message,
                help);
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
                    .OrderByDescending(x => x.Score)
                    .ToList();

            int place = 1;

            foreach (Team team in ranking)
            {
                result +=
                    $"{place}. {team.Emoji} " +
                    $"{team.Name} — " +
                    $"{team.Score} نقطة\n";

                place++;
            }

            if (ranking.Count > 0)
            {
                Team winner = ranking[0];

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
            return value.Trim().ToLowerInvariant()
                switch
            {
                "احمر" => "الأمراء",
                "الأحمر" => "الأمراء",

                "ازرق" => "النجوم",
                "الأزرق" => "النجوم",

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
                out number);
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
            }

            return userId;
        }
    }

    // =================================================================
    // MazajGame
    // =================================================================

    public class MazajGame
    {
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
            PointsPerCard = pointsPerCard;
            TeamCount = teamCount;

            TurnOrder =
                new List<string>();

            Cards = CreateCards();

            List<Team> allTeams =
                new List<Team>
                {
                    new Team("الأمراء", "🟥"),
                    new Team("النجوم", "🟦")
                };

            Teams = allTeams
                .Take(2)
                .ToList();
        }

        public string CurrentPlayerId
        {
            get
            {
                if (TurnOrder.Count == 0)
                    return "";

                if (CurrentPlayerIndex < 0 ||
                    CurrentPlayerIndex >=
                    TurnOrder.Count)
                {
                    return "";
                }

                return TurnOrder[
                    CurrentPlayerIndex];
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

        public Team? GetTeamByPlayer(
            string userId)
        {
            return Teams.FirstOrDefault(
                x => x.Players.ContainsKey(userId));
        }

        public bool AllCardsUsed
        {
            get
            {
                return Cards.All(
                    x => x.Used);
            }
        }

        // =========================================================
        // البطاقات
        // =========================================================

        private static List<Card> CreateCards()
        {
            List<string> names =
                new List<string>
                {
                    "ضربة الوحش محمد 🇮🇶❤️",
                    "ضربة يوسف المهندس",
                    "ضربة سرمد الوحش 🔥",
                    "ضربة ابو عماد",
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
                    "الحظ الجميل",
                    "مفاجأة مزاج",
                    "الضربة الأخيرة",
                    "ضربة البرق",
                    "ضربة النار",
                    "ضربة الصدمة",
                    "الضربة السرية",
                    "بطاقة الحظ",
                    "بطاقة المفاجأة",
                    "الضربة الكبرى"
                };

            while (names.Count < 57)
            {
                names.Add("بطاقة مزاج");
            }

            names.AddRange(
                new[]
                {
                    "بطاقة نحس 58",
                    "بطاقة نحس 59",
                    "بطاقة نحس 60",
                    "بطاقة نحس 61",
                    "بطاقة نحس 62",
                    "بطاقة نحس 63",
                    "بطاقة نحس 64",
                    "بطاقة نحس 65"
                });

            List<Card> cards =
                new List<Card>();

            int[] positiveValues =
                new int[57];

            positiveValues[0] = 100;
            positiveValues[1] = 90;
            positiveValues[2] = 85;

            int[] remaining =
            {
                80, 80,
                75, 75,
                70, 70,
                65, 65,
                60, 60,
                55, 55,
                50, 50,
                45, 45,
                40, 40,
                35, 35,
                30, 30,
                25, 25,
                20, 20,
                15, 15,
                10, 10,
                5, 5
            };

            for (int i = 3; i < 57; i++)
            {
                positiveValues[i] =
                    remaining[
                        (i - 3) %
                        remaining.Length];
            }

            for (int i = 0; i < 57; i++)
            {
                cards.Add(
                    new Card(
                        i + 1,
                        names[i],
                        positiveValues[i]));
            }

            int[] negativeValues =
            {
                -20,
                -20,
                -25,
                -25,
                -30,
                -30,
                -40,
                -50
            };

            for (int i = 57; i < 65; i++)
            {
                cards.Add(
                    new Card(
                        i + 1,
                        names[i],
                        negativeValues[
                            i - 57]));
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
