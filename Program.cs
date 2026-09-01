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
            string email =
                Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";

            string password =
                Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("WOLF_EMAIL أو WOLF_PASSWORD غير موجود.");
                return;
            }

            _client = new WolfClient();

            _client.OnConnected += client =>
            {
                Console.WriteLine("WOLF BOT CONNECTED!");
            };

            _client.OnDisconnected += (client, error) =>
            {
                Console.WriteLine("WOLF BOT DISCONNECTED: " + error);
            };

            _client.OnConnectionError += (client, error) =>
            {
                Console.WriteLine("WOLF CONNECTION ERROR: " + error);
            };

            // استقبال رسائل ولف
            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    string text = message.Content?.Trim() ?? "";

                    Console.WriteLine(
                        $"MESSAGE | User: {message.UserId} | " +
                        $"Group: {message.GroupId} | " +
                        $"Text: {text}"
                    );

                    if (!text.StartsWith("!مزاج", StringComparison.OrdinalIgnoreCase))
                        return;

                    string command = text.Length > 5
                        ? text.Substring(5).Trim()
                        : "";

                    await HandleCommand(client, message, command);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("COMMAND ERROR: " + ex);
                }
            };

            Console.WriteLine("جاري تسجيل الدخول...");

            bool loggedIn = await _client.Login(email, password);

            Console.WriteLine(
                loggedIn
                    ? "LOGIN SUCCESS!"
                    : "LOGIN FAILED!"
            );

            if (!loggedIn)
                return;

            Console.WriteLine("بوت مزاج جاهز.");

            await Task.Delay(-1);
        }

        private static async Task HandleCommand(
            IWolfClient client,
            Message message,
            string command)
        {
            string[] parts = command
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                await SendHelp(client, message);
                return;
            }

            string subCommand = parts[0];

            switch (subCommand)
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
                    await ChooseCard(client, message, parts);
                    break;

                case "بطاقات":
                    await ShowCards(client, message);
                    break;

                case "مساعدة":
                    await SendHelp(client, message);
                    break;

                default:
                    await client.Reply(
                        message,
                        "❌ الأمر غير معروف.\nاكتب !مزاج مساعدة"
                    );
                    break;
            }
        }

        // =========================
        // إنشاء اللعبة
        // =========================

        private static async Task NewGame(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (parts.Length < 3)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام الصحيح:\n" +
                    "!مزاج جديد <النقاط> <عدد الفرق>\n\n" +
                    "مثال:\n" +
                    "!مزاج جديد 50 4"
                );
                return;
            }

            if (!TryParseNumber(parts[1], out int points) ||
                !TryParseNumber(parts[2], out int teamCount))
            {
                await client.Reply(
                    message,
                    "❌ يجب أن تكون النقاط وعدد الفرق أرقامًا."
                );
                return;
            }

            if (points <= 0)
            {
                await client.Reply(
                    message,
                    "❌ النقاط يجب أن تكون أكبر من صفر."
                );
                return;
            }

            if (teamCount < 2 || teamCount > 4)
            {
                await client.Reply(
                    message,
                    "❌ عدد الفرق يجب أن يكون من 2 إلى 4."
                );
                return;
            }

            _game = new MazajGame(points, teamCount);

            string teams =
                string.Join(
                    "\n",
                    _game.Teams.Select(t => $"{t.Emoji} {t.Name}")
                );

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة مزاج 🔥🎭\n\n" +
                "🃏 عدد البطاقات: 65\n" +
                $"💰 مدى النقاط العشوائية: ±{points}\n" +
                $"👥 عدد الفرق: {teamCount}\n\n" +
                "الفرق:\n" +
                teams +
                "\n\n" +
                "للانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n" +
                "!مزاج انضم اصفر\n" +
                "!مزاج انضم بنفسجي\n\n" +
                "بعد اكتمال الفرق اكتب:\n" +
                "!مزاج بدء"
            );
        }

        // =========================
        // الانضمام
        // =========================

        private static async Task JoinTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await client.Reply(message, "❌ لا توجد لعبة حاليًا.");
                return;
            }

            if (_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ اللعبة بدأت، لا يمكن الانضمام الآن."
                );
                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ اختر لون الفريق:\n" +
                    "احمر | ازرق | اصفر | بنفسجي"
                );
                return;
            }

            Team? team = _game.FindTeam(parts[1]);

            if (team == null)
            {
                await client.Reply(
                    message,
                    "❌ هذا الفريق غير موجود في اللعبة."
                );
                return;
            }

            if (_game.GetPlayerTeam(message.UserId) != null)
            {
                await client.Reply(
                    message,
                    "⚠️ أنت منضم إلى فريق بالفعل.\n" +
                    "استخدم:\n!مزاج تغيير <اللون>"
                );
                return;
            }

            string nickname = await GetNickname(client, message.UserId);

            team.Players[message.UserId] = nickname;

            await client.Reply(
                message,
                $"✅ انضم {nickname} إلى فريق {team.Emoji} {team.Name}"
            );
        }

        // =========================
        // تغيير الفريق
        // =========================

        private static async Task ChangeTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await client.Reply(message, "❌ لا توجد لعبة حاليًا.");
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
                    "❌ الاستخدام:\n!مزاج تغيير <اللون>"
                );
                return;
            }

            Team? oldTeam = _game.GetPlayerTeam(message.UserId);
            Team? newTeam = _game.FindTeam(parts[1]);

            if (newTeam == null)
            {
                await client.Reply(
                    message,
                    "❌ الفريق غير موجود."
                );
                return;
            }

            if (oldTeam == null)
            {
                await client.Reply(
                    message,
                    "❌ أنت غير منضم إلى أي فريق."
                );
                return;
            }

            if (oldTeam == newTeam)
            {
                await client.Reply(
                    message,
                    "⚠️ أنت أصلًا في هذا الفريق."
                );
                return;
            }

            string nickname =
                oldTeam.Players[message.UserId];

            oldTeam.Players.Remove(message.UserId);
            newTeam.Players[message.UserId] = nickname;

            await client.Reply(
                message,
                $"🔄 تم تغيير فريقك إلى {newTeam.Emoji} {newTeam.Name}"
            );
        }

        // =========================
        // عرض اللاعبين
        // =========================

        private static async Task ShowPlayers(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(message, "❌ لا توجد لعبة.");
                return;
            }

            string result = "👥 لاعبين لعبة مزاج:\n\n";

            foreach (Team team in _game.Teams)
            {
                result +=
                    $"{team.Emoji} {team.Name} — {team.Score} نقطة\n";

                if (team.Players.Count == 0)
                {
                    result += "   لا يوجد لاعبين\n";
                }
                else
                {
                    foreach (string name in team.Players.Values)
                    {
                        result += $"   👤 {name}\n";
                    }
                }

                result += "\n";
            }

            await client.Reply(message, result);
        }

        // =========================
        // بدء اللعبة
        // =========================

        private static async Task StartGame(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(message, "❌ لا توجد لعبة.");
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

            foreach (Team team in _game.Teams)
            {
                if (team.Players.Count == 0)
                {
                    await client.Reply(
                        message,
                        $"❌ فريق {team.Emoji} {team.Name} لا يحتوي على لاعبين."
                    );
                    return;
                }
            }

            _game.Started = true;
            _game.CurrentTeamIndex = 0;

            Team firstTeam = _game.Teams[0];

            await client.Reply(
                message,
                "🎭🔥 بدأت لعبة مزاج 🔥🎭\n\n" +
                "🃏 البطاقات: 1 - 65\n" +
                "🚫 ممنوع سرقة البطاقات\n" +
                "🎯 كل فريق يختار بطاقة في دوره\n\n" +
                $"🎲 الدور الآن على فريق:\n" +
                $"{firstTeam.Emoji} {firstTeam.Name}\n\n" +
                "استخدموا:\n" +
                "!مزاج اختار <رقم>"
            );
        }

        // =========================
        // اختيار البطاقة
        // =========================

        private static async Task ChooseCard(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await client.Reply(message, "❌ لا توجد لعبة.");
                return;
            }

            if (!_game.Started)
            {
                await client.Reply(
                    message,
                    "❌ اللعبة لم تبدأ بعد."
                );
                return;
            }

            if (parts.Length < 2)
            {
                await client.Reply(
                    message,
                    "❌ الاستخدام:\n!مزاج اختار <رقم البطاقة>"
                );
                return;
            }

            if (!TryParseNumber(parts[1], out int cardNumber))
            {
                await client.Reply(
                    message,
                    "❌ رقم البطاقة غير صحيح."
                );
                return;
            }

            if (cardNumber < 1 || cardNumber > 65)
            {
                await client.Reply(
                    message,
                    "❌ رقم البطاقة يجب أن يكون بين 1 و65."
                );
                return;
            }

            Team? playerTeam =
                _game.GetPlayerTeam(message.UserId);

            if (playerTeam == null)
            {
                await client.Reply(
                    message,
                    "❌ لا يمكنك اختيار بطاقة لأنك لست ضمن أي فريق."
                );
                return;
            }

            Team currentTeam =
                _game.Teams[_game.CurrentTeamIndex];

            if (playerTeam != currentTeam)
            {
                await client.Reply(
                    message,
                    $"⛔ مو دور فريقك.\n" +
                    $"🎯 الدور الآن على {currentTeam.Emoji} {currentTeam.Name}"
                );
                return;
            }

            Card card = _game.Cards[cardNumber - 1];

            if (card.Used)
            {
                await client.Reply(
                    message,
                    $"❌ البطاقة رقم {cardNumber} تم اختيارها سابقًا."
                );
                return;
            }

            card.Used = true;

            currentTeam.Score += card.Value;

            string valueText =
                card.Value >= 0
                    ? $"+{card.Value}"
                    : card.Value.ToString();

            string result =
                "🎴💥 تم كشف البطاقة 💥🎴\n\n" +
                $"🃏 البطاقة: {card.Number}\n" +
                $"🎭 الاسم: {card.Name}\n" +
                $"💰 القيمة: {valueText}\n\n" +
                $"🏆 فريق {currentTeam.Emoji} {currentTeam.Name}\n" +
                $"📊 النقاط الحالية: {currentTeam.Score}";

            _game.CurrentTeamIndex++;

            if (_game.CurrentTeamIndex >= _game.Teams.Count)
                _game.CurrentTeamIndex = 0;

            if (_game.AllCardsUsed)
            {
                _game.Started = false;

                result +=
                    "\n\n🏁🔥 انتهت اللعبة! 🔥🏁\n\n" +
                    BuildFinalResults();

                _game = null;
            }
            else
            {
                Team nextTeam =
                    _game.Teams[_game.CurrentTeamIndex];

                result +=
                    "\n\n🎯 الدور الآن على:\n" +
                    $"{nextTeam.Emoji} {nextTeam.Name}\n\n" +
                    "!مزاج اختار <رقم>";
            }

            await client.Reply(message, result);
        }

        // =========================
        // البطاقات
        // =========================

        private static async Task ShowCards(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await client.Reply(message, "❌ لا توجد لعبة.");
                return;
            }

            List<Card> used =
                _game.Cards
                    .Where(c => c.Used)
                    .ToList();

            if (used.Count == 0)
            {
                await client.Reply(
                    message,
                    "🃏 لم يتم اختيار أي بطاقة بعد.\n" +
                    "البطاقات المتاحة: 65"
                );
                return;
            }

            string result =
                $"🃏 البطاقات المكشوفة: {used.Count}/65\n\n";

            foreach (Card card in used)
            {
                string value =
                    card.Value >= 0
                        ? $"+{card.Value}"
                        : card.Value.ToString();

                result +=
                    $"{card.Number}. {card.Name} = {value}\n";
            }

            int remaining = 65 - used.Count;

            result +=
                $"\n🎴 المتبقي: {remaining} بطاقة";

            await client.Reply(message, result);
        }

        // =========================
        // المساعدة
        // =========================

        private static async Task SendHelp(
            IWolfClient client,
            Message message)
        {
            await client.Reply(
                message,
                "🎭🔥 أوامر لعبة مزاج 🔥🎭\n\n" +
                "🎮 إنشاء لعبة:\n" +
                "!مزاج جديد <النقاط> <عدد الفرق>\n\n" +
                "👥 الانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق\n" +
                "!مزاج انضم اصفر\n" +
                "!مزاج انضم بنفسجي\n\n" +
                "🔄 تغيير الفريق:\n" +
                "!مزاج تغيير <اللون>\n\n" +
                "👥 اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +
                "🚀 بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +
                "🎴 اختيار بطاقة:\n" +
                "!مزاج اختار 13\n" +
                "!مزاج اختار ١٣\n\n" +
                "🃏 البطاقات المكشوفة:\n" +
                "!مزاج بطاقات\n\n" +
                "❓ المساعدة:\n" +
                "!مزاج مساعدة"
            );
        }

        // =========================
        // النتائج
        // =========================

        private static string BuildFinalResults()
        {
            if (_game == null)
                return "";

            string result = "";

            foreach (Team team in _game.Teams
                         .OrderByDescending(t => t.Score))
            {
                result +=
                    $"{team.Emoji} {team.Name}: {team.Score} نقطة\n";
            }

            Team winner =
                _game.Teams
                    .OrderByDescending(t => t.Score)
                    .First();

            List<Team> winners =
                _game.Teams
                    .Where(t => t.Score == winner.Score)
                    .ToList();

            if (winners.Count == 1)
            {
                result +=
                    $"\n🏆👑 الفائز:\n" +
                    $"{winner.Emoji} {winner.Name}\n" +
                    $"🎉 برصيد {winner.Score} نقطة!";
            }
            else
            {
                result += "\n🤝 تعادل بين:\n";

                foreach (Team team in winners)
                {
                    result +=
                        $"{team.Emoji} {team.Name}\n";
                }
            }

            return result;
        }

        // =========================
        // الأرقام العربية والإنكليزية
        // =========================

        private static bool TryParseNumber(
            string text,
            out int number)
        {
            string normalized = NormalizeArabicDigits(text);

            return int.TryParse(
                normalized,
                out number
            );
        }

        private static string NormalizeArabicDigits(
            string text)
        {
            return text
                .Replace('٠', '0')
                .Replace('١', '1')
                .Replace('٢', '2')
                .Replace('٣', '3')
                .Replace('٤', '4')
                .Replace('٥', '5')
                .Replace('٦', '6')
                .Replace('٧', '7')
                .Replace('٨', '8')
                .Replace('٩', '9');
        }

        private static async Task<string> GetNickname(
            IWolfClient client,
            string userId)
        {
            try
            {
                User user = await client.GetUser(userId);

                return string.IsNullOrWhiteSpace(user.Nickname)
                    ? userId
                    : user.Nickname;
            }
            catch
            {
                return userId;
            }
        }
    }

    // =====================================================
    // اللعبة
    // =====================================================

    public class MazajGame
    {
        public List<Team> Teams { get; } = new();
        public List<Card> Cards { get; } = new();

        public bool Started { get; set; }
        public int CurrentTeamIndex { get; set; }

        private readonly int _points;
        private readonly Random _random = new();

        public bool AllCardsUsed =>
            Cards.All(c => c.Used);

        public MazajGame(
            int points,
            int teamCount)
        {
            _points = points;

            AddTeam("احمر", "🔴");
            AddTeam("ازرق", "🔵");
            AddTeam("اصفر", "🟡");
            AddTeam("بنفسجي", "🟣");

            while (Teams.Count > teamCount)
                Teams.RemoveAt(Teams.Count - 1);

            CreateCards();
        }

        private void AddTeam(
            string name,
            string emoji)
        {
            Teams.Add(
                new Team
                {
                    Name = name,
                    Emoji = emoji
                }
            );
        }

        public Team? FindTeam(string name)
        {
            string normalized = name.Trim();

            return Teams.FirstOrDefault(
                t => t.Name.Equals(
                    normalized,
                    StringComparison.OrdinalIgnoreCase
                )
            );
        }

        public Team? GetPlayerTeam(string userId)
        {
            return Teams.FirstOrDefault(
                t => t.Players.ContainsKey(userId)
            );
        }

        // =================================================
        // إنشاء 65 بطاقة
        // =================================================

        private void CreateCards()
        {
            List<string> names = new()
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

                "ضربة سند سوريا 1",
                "ضربة سند سوريا 2",
                "ضربة سند سوريا 3",
                "ضربة سند سوريا 4",
                "ضربة سند سوريا 5",
                "ضربة سند سوريا 6",
                "ضربة سند سوريا 7",
                "ضربة سند سوريا 8",
                "ضربة سند سوريا 9",
                "ضربة سند سوريا 10",
                "ضربة سند سوريا 11",
                "ضربة سند سوريا 12",
                "ضربة سند سوريا 13",
                "ضربة سند سوريا 14",
                "ضربة سند سوريا 15",
                "ضربة سند سوريا 16",
                "ضربة سند سوريا 17",
                "ضربة سند سوريا 18",
                "ضربة سند سوريا 19",
                "ضربة سند سوريا 20",
                "ضربة سند سوريا 21",
                "ضربة سند سوريا 22",
                "ضربة سند سوريا 23",
                "ضربة سند سوريا 24",
                "ضربة سند سوريا 25",
                "ضربة سند سوريا 26",
                "ضربة سند سوريا 27",
                "ضربة سند سوريا 28",
                "ضربة سند سوريا 29",
                "ضربة سند سوريا 30",

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

            // ضمان 65 بطاقة بالضبط
            while (names.Count < 65)
                names.Add("بطاقة مزاج");

            if (names.Count > 65)
                names = names.Take(65).ToList();

            // خلط الأسماء عشوائيًا
            names = names
                .OrderBy(_ => _random.Next())
                .ToList();

            for (int i = 0; i < 65; i++)
            {
                string name = names[i];

                int value;

                // البطاقات ذات القيم الثابتة
                if (name == "ضربة الوحش محمد 🇮🇶❤️")
                {
                    value = 100;
                }
                else if (name == "هولو وئام الفگر")
                {
                    value = -100;
                }
                else
                {
                    value = RandomValue();
                }

                Cards.Add(
                    new Card
                    {
                        Number = i + 1,
                        Name = name,
                        Value = value,
                        Used = false
                    }
                );
            }
        }

        private int RandomValue()
        {
            int value;

            do
            {
                value = _random.Next(
                    -_points,
                    _points + 1
                );
            }
            while (value == 0);

            return value;
        }
    }

    // =====================================================
    // الفريق
    // =====================================================

    public class Team
    {
        public string Name { get; set; } = "";
        public string Emoji { get; set; } = "";

        public int Score { get; set; }

        public Dictionary<string, string> Players { get; } = new();
    }

    // =====================================================
    // البطاقة
    // =====================================================

    public class Card
    {
        public int Number { get; set; }
        public string Name { get; set; } = "";
        public int Value { get; set; }
        public bool Used { get; set; }
    }
}
