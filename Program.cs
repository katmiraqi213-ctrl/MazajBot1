using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Commands;

namespace CSharpConsoleApp
{
    public class Program
    {
        private static IWolfClient? _client;

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
                    "WOLF_EMAIL و WOLF_PASSWORD غير موجودين."
                );

                return;
            }

            _client = new WolfClient()
                .SetupCommands()
                .WithCommandSet(c =>
                {
                    c.AddCommands<MazajGameCommands>()
                     .WithPrefix("!");
                })
                .WithSerilog()
                .Done();

            _client.OnConnected += (_) =>
            {
                Console.WriteLine(
                    "بوت مزاج متصل بـ WOLF!"
                );

                return Task.CompletedTask;
            };

            var result =
                await _client.Login(email, password);

            Console.WriteLine(
                result
                    ? "تم تسجيل الدخول بنجاح!"
                    : "فشل تسجيل الدخول - تأكد من بيانات الحساب"
            );

            await Task.Delay(-1);
        }
    }

    // ==============================
    // بطاقة مزاج
    // ==============================

    public class MazajCard
    {
        public int Number { get; set; }

        public string Name { get; set; } = "";

        public int Points { get; set; }

        public bool Picked { get; set; }
    }

    // ==============================
    // لعبة مزاج
    // ==============================

    public class MazajGame
    {
        public int TargetPoints;

        public int TeamCount;

        public List<string> TeamNames = new();

        public Dictionary<string, List<string>> Teams = new();

        public Dictionary<string, int> Scores = new();

        public List<MazajCard> Cards = new();

        public bool Started;

        public int CurrentTeamIndex;

        public string CurrentTeam
        {
            get
            {
                if (TeamNames.Count == 0)
                    return "";

                if (CurrentTeamIndex >= TeamNames.Count)
                    CurrentTeamIndex = 0;

                return TeamNames[CurrentTeamIndex];
            }
        }

        public int TotalJoined =>
            Teams.Values.Sum(t => t.Count);

        public int RemainingCards =>
            Cards.Count(c => !c.Picked);

        public void NextTurn()
        {
            if (TeamNames.Count == 0)
                return;

            CurrentTeamIndex =
                (CurrentTeamIndex + 1) %
                TeamNames.Count;
        }
    }

    // ==============================
    // أوامر مزاج
    // ==============================

    public class MazajGameCommands : WolfContext
    {
        private static readonly Dictionary<long, MazajGame> Games =
            new();

        private static readonly Random Random =
            new();

        private static readonly string[] AllTeams =
        {
            "احمر",
            "ازرق",
            "اصفر",
            "بنفسجي"
        };

        // ==============================
        // البطاقات الخاصة
        // ==============================

        private static readonly Dictionary<string, int> SpecialCards =
            new()
            {
                { "ضربة الوحش محمد 🇮🇶❤️", 100 },

                { "هولو وئام الفگر", -100 },

                { "طاحج حضج توت 😂", -50 },
                { "صخام بوجهك ايهاب", -50 },
                { "سراوي تيتي لاتحل ولا تربط", -50 },
                { "هذا حظ زوز", -50 },
                { "لولو التعبانه", -50 },
                { "نواره السلبيه", -50 },

                { "ضربة ابو حامد", -75 },
                { "ضربة حمدي الوزير", -75 },
                { "ضربة حيدر بنكه", -75 },
                { "ضربة جمو موسيقى", -75 },

                { "ضربة اساور صاروخ باليستي", 100 },
                { "صاروخ ارض ارض", 100 },
                { "ضربة علي القويه", 100 },
                { "ضربة ابو جنه", 100 }
            };

        // ==============================
        // !مزاج
        // ==============================

        [Command("مزاج")]
        public async Task HandleMazaj(string message)
        {
            long chatId = SourceId;

            string userName =
                SourceSubscriber?.Name ?? "لاعب";

            string[] parts =
                (message ?? "")
                    .Trim()
                    .Split(
                        ' ',
                        StringSplitOptions.RemoveEmptyEntries
                    );

            // بدون أمر
            if (parts.Length == 0)
            {
                await SendReply(
                    "🎭 أوامر مزاج 🎭\n\n" +

                    "!مزاج جديد <النقاط> <عدد الفرق>\n" +

                    "!مزاج انضم <اللون>\n" +

                    "!مزاج تغيير <اللون>\n" +

                    "!مزاج لاعبين\n" +

                    "!مزاج بدء\n" +

                    "!مزاج <رقم البطاقة>\n" +

                    "!مزاج بطاقات\n" +

                    "!مزاج مساعدة"
                );

                return;
            }

            string action = parts[0];

            // =====================================
            // اختيار البطاقة بالرقم مباشرة
            // مثال:
            // !مزاج 13
            // !مزاج ١٣
            // =====================================

            if (int.TryParse(
                    ToEnglishNumbers(action),
                    out int directCardNumber))
            {
                await PickCard(
                    chatId,
                    directCardNumber,
                    userName
                );

                return;
            }

            // =====================================
            // باقي الأوامر
            // =====================================

            switch (action)
            {
                case "جديد":

                    await CreateGame(
                        chatId,
                        parts
                    );

                    break;

                case "انضم":

                case "تغيير":

                    await JoinTeam(
                        chatId,
                        parts,
                        userName
                    );

                    break;

                case "لاعبين":

                    await ShowPlayers(chatId);

                    break;

                case "بدء":

                    await StartGame(chatId);

                    break;

                case "بطاقات":

                    await ShowCards(chatId);

                    break;

                case "مساعدة":

                    await ShowHelp();

                    break;

                default:

                    await SendReply(
                        "❌ الأمر غير معروف.\n\n" +
                        "اكتب !مزاج مساعدة"
                    );

                    break;
            }
        }

        // ==============================
        // إنشاء اللعبة
        // ==============================

        private async Task CreateGame(
            long chatId,
            string[] parts)
        {
            if (parts.Length < 3 ||
                !int.TryParse(
                    ToEnglishNumbers(parts[1]),
                    out int points) ||
                !int.TryParse(
                    ToEnglishNumbers(parts[2]),
                    out int teamCount))
            {
                await SendReply(
                    "الصيغة الصحيحة:\n\n" +
                    "!مزاج جديد <النقاط> <عدد الفرق>\n\n" +
                    "مثال:\n" +
                    "!مزاج جديد 400 4"
                );

                return;
            }

            if (points <= 0)
            {
                await SendReply(
                    "❌ النقاط لازم تكون أكبر من صفر."
                );

                return;
            }

            if (teamCount < 2 ||
                teamCount > 4)
            {
                await SendReply(
                    "❌ عدد الفرق لازم يكون بين 2 و4."
                );

                return;
            }

            var game = new MazajGame
            {
                TargetPoints = points,

                TeamCount = teamCount,

                Started = false,

                CurrentTeamIndex = 0
            };

            for (int i = 0;
                 i < teamCount;
                 i++)
            {
                string team =
                    AllTeams[i];

                game.TeamNames.Add(team);

                game.Teams[team] =
                    new List<string>();

                game.Scores[team] = 0;
            }

            CreateCards(game);

            Games[chatId] =
                game;

            await SendReply(
                "🎭🔥 لعبة مزاج جديدة 🔥🎭\n\n" +

                $"🎯 النقاط المطلوبة: {points}\n" +

                $"👥 الفرق: " +
                $"{string.Join(" - ", game.TeamNames)}\n" +

                "🃏 عدد البطاقات: 65\n\n" +

                "للانضمام:\n" +

                "!مزاج انضم <اللون>\n\n" +

                "مثال:\n" +

                "!مزاج انضم احمر"
            );
        }

        // ==============================
        // إنشاء 65 بطاقة
        // ==============================

        private static void CreateCards(
            MazajGame game)
        {
            game.Cards.Clear();

            var names =
                new List<string>();

            // إضافة البطاقات الخاصة
            foreach (var card in SpecialCards)
            {
                names.Add(card.Key);
            }

            // البطاقات العادية
            int normalNumber = 1;

            while (names.Count < 65)
            {
                string name;

                do
                {
                    name =
                        Random.Next(0, 2) == 0

                        ? $"ضربة عشوائية {normalNumber}"

                        : $"بطاقة حظ {normalNumber}";

                    normalNumber++;

                }
                while (names.Contains(name));

                names.Add(name);
            }

            // خلط البطاقات
            names =
                names
                    .OrderBy(_ => Random.Next())
                    .ToList();

            int[] normalValues =
            {
                -100,
                -75,
                -50,
                -25,
                25,
                50,
                75,
                100
            };

            for (int i = 0;
                 i < 65;
                 i++)
            {
                string name =
                    names[i];

                int value;

                if (SpecialCards.TryGetValue(
                        name,
                        out int specialValue))
                {
                    value =
                        specialValue;
                }
                else
                {
                    value =
                        normalValues[
                            Random.Next(
                                normalValues.Length)
                        ];
                }

                game.Cards.Add(
                    new MazajCard
                    {
                        Number = i + 1,

                        Name = name,

                        Points = value,

                        Picked = false
                    }
                );
            }
        }

        // ==============================
        // الانضمام للفريق
        // ==============================

        private async Task JoinTeam(
            long chatId,
            string[] parts,
            string userName)
        {
            if (!Games.TryGetValue(
                    chatId,
                    out var game))
            {
                await SendReply(
                    "❌ ماكو لعبة شغالة حالياً.\n\n" +
                    "اكتب:\n" +
                    "!مزاج جديد 400 4"
                );

                return;
            }

            if (game.Started)
            {
                await SendReply(
                    "❌ اللعبة بدت، ما تكدر تنضم أو تغير الفريق."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await SendReply(
                    "حدد الفريق:\n" +
                    string.Join(
                        " - ",
                        game.TeamNames)
                );

                return;
            }

            string team =
                parts[1];

            if (!game.Teams.ContainsKey(team))
            {
                await SendReply(
                    "❌ هذا الفريق غير موجود.\n\n" +
                    "المتاح:\n" +
                    string.Join(
                        " - ",
                        game.TeamNames)
                );

                return;
            }

            // إزالة اللاعب من أي فريق سابق
            foreach (var players in game.Teams.Values)
            {
                players.Remove(userName);
            }

            game.Teams[team]
                .Add(userName);

            await SendReply(
                $"👤 {userName}\n" +
                $"✅ انضم إلى فريق {team}"
            );
        }

        // ==============================
        // عرض اللاعبين
        // ==============================

        private async Task ShowPlayers(
            long chatId)
        {
            if (!Games.TryGetValue(
                    chatId,
                    out var game))
            {
                await SendReply(
                    "❌ ماكو لعبة شغالة حالياً."
                );

                return;
            }

            string result =
                $"👥 اللاعبين ({game.TotalJoined})\n\n";

            foreach (string team in game.TeamNames)
            {
                var players =
                    game.Teams[team];

                result +=
                    $"🔹 {team} " +
                    $"({players.Count}): " +

                    (
                        players.Count == 0
                            ? "لا يوجد"
                            : string.Join(
                                "، ",
                                players)
                    ) +

                    "\n";
            }

            await SendReply(result);
        }

        // ==============================
        // بدء اللعبة
        // ==============================

        private async Task StartGame(
            long chatId)
        {
            if (!Games.TryGetValue(
                    chatId,
                    out var game))
            {
                await SendReply(
                    "❌ ماكو لعبة شغالة حالياً."
                );

                return;
            }

            if (game.Started)
            {
                await SendReply(
                    "❌ اللعبة بدأت مسبقاً."
                );

                return;
            }

            var activeTeams =
                game.TeamNames
                    .Where(
                        t =>
                            game.Teams[t].Count > 0
                    )
                    .ToList();

            if (activeTeams.Count < 2)
            {
                await SendReply(
                    "❌ لازم يكون أكو لاعبين بفريقين على الأقل."
                );

                return;
            }

            game.TeamNames =
                activeTeams;

            game.Started =
                true;

            game.CurrentTeamIndex =
                0;

            await SendReply(
                "🎭🔥 اللعبة بدأت 🔥🎭\n\n" +

                $"🎯 الدور الآن على فريق: " +
                $"{game.CurrentTeam}\n\n" +

                "🃏 اختاروا بطاقة بإرسال رقمها فقط:\n\n" +

                "!مزاج 13\n" +

                "أو:\n" +

                "!مزاج ١٣"
            );
        }

        // ==============================
        // اختيار البطاقة
        // ==============================

        private async Task PickCard(
            long chatId,
            int cardNumber,
            string userName)
        {
            if (!Games.TryGetValue(
                    chatId,
                    out var game))
            {
                await SendReply(
                    "❌ ماكو لعبة شغالة حالياً."
                );

                return;
            }

            if (!game.Started)
            {
                await SendReply(
                    "❌ اللعبة لسه ما بدت.\n\n" +
                    "اكتب:\n" +
                    "!مزاج بدء"
                );

                return;
            }

            string playerTeam =
                game.Teams
                    .FirstOrDefault(
                        t =>
                            t.Value.Contains(
                                userName))
                    .Key;

            if (string.IsNullOrEmpty(
                    playerTeam))
            {
                await SendReply(
                    "❌ أنت مو منضم لأي فريق."
                );

                return;
            }

            if (playerTeam !=
                game.CurrentTeam)
            {
                await SendReply(
                    $"⏳ استنى دورك!\n\n" +
                    $"🎯 الدور الآن على فريق " +
                    $"{game.CurrentTeam}"
                );

                return;
            }

            if (cardNumber < 1 ||
                cardNumber > 65)
            {
                await SendReply(
                    "❌ رقم البطاقة لازم يكون بين 1 و65."
                );

                return;
            }

            MazajCard? card =
                game.Cards
                    .FirstOrDefault(
                        c =>
                            c.Number ==
                            cardNumber);

            if (card == null)
            {
                await SendReply(
                    "❌ البطاقة غير موجودة."
                );

                return;
            }

            if (card.Picked)
            {
                await SendReply(
                    "❌ هذه البطاقة مأخوذة.\n" +
                    "اختار رقم بطاقة ثانية."
                );

                return;
            }

            // تسجيل البطاقة
            card.Picked =
                true;

            // إضافة النقاط
            game.Scores[playerTeam] +=
                card.Points;

            string pointsText =
                card.Points >= 0
                    ? $"+{card.Points}"
                    : card.Points.ToString();

            await SendReply(
                $"🃏 بطاقة رقم {card.Number}\n\n" +

                $"🎭 {card.Name}\n" +

                $"💰 النقاط: {pointsText}\n\n" +

                $"🏳️ فريق {playerTeam}\n" +

                $"📊 الرصيد: " +
                $"{game.Scores[playerTeam]} نقطة"
            );

            // الفوز عند الوصول للنقاط المطلوبة
            if (game.Scores[playerTeam] >=
                game.TargetPoints)
            {
                await EndGame(chatId);

                return;
            }

            // إذا خلصت البطاقات
            if (game.RemainingCards == 0)
            {
                await EndGame(chatId);

                return;
            }

            // الدور للفريق التالي
            game.NextTurn();

            await SendReply(
                $"🔄 الدور الآن على فريق: " +
                $"{game.CurrentTeam}"
            );
        }

        // ==============================
        // عرض البطاقات
        // ==============================

        private async Task ShowCards(
            long chatId)
        {
            if (!Games.TryGetValue(
                    chatId,
                    out var game))
            {
                await SendReply(
                    "❌ ماكو لعبة شغالة حالياً."
                );

                return;
            }

            string result =
                "🃏 بطاقات مزاج\n\n";

            foreach (var card in game.Cards)
            {
                result +=
                    card.Picked

                        ? $"❌ {card.Number}\n"

                        : $"🟢 {card.Number}\n";
            }

            result +=
                $"\n📦 البطاقات المتبقية: " +
                $"{game.RemainingCards}";

            if (game.Started)
            {
                result +=
                    $"\n🎯 الدور: " +
                    $"{game.CurrentTeam}";
            }

            await SendReply(result);
        }

        // ==============================
        // نهاية اللعبة
        // ==============================

        private async Task EndGame(
            long chatId)
        {
            if (!Games.TryGetValue(
                    chatId,
                    out var game))
            {
                return;
            }

            var standings =
                game.Scores
                    .OrderByDescending(
                        x => x.Value)
                    .ToList();

            string winner =
                standings[0].Key;

            string scores =
                string.Join(
                    "\n",
                    standings.Select(
                        x =>
                            $"🏆 {x.Key}: " +
                            $"{x.Value} نقطة"
                    )
                );

            await SendReply(
                "🏁🎭 انتهت لعبة مزاج 🎭🏁\n\n" +

                "📊 النتائج:\n\n" +

                scores +

                "\n\n" +

                $"🥇 الفريق الفائز: {winner}"
            );

            Games.Remove(chatId);
        }

        // ==============================
        // المساعدة
        // ==============================

        private async Task ShowHelp()
        {
            await SendReply(
                "🎭🔥 أوامر بوت مزاج 🔥🎭\n\n" +

                "🆕 إنشاء لعبة:\n" +
                "!مزاج جديد 400 4\n\n" +

                "👥 الانضمام:\n" +
                "!مزاج انضم احمر\n\n" +

                "🔄 تغيير الفريق:\n" +
                "!مزاج تغيير ازرق\n\n" +

                "👤 اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +

                "▶️ بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +

                "🃏 اختيار بطاقة:\n" +
                "!مزاج 13\n" +
                "!مزاج ١٣\n\n" +

                "📋 عرض البطاقات:\n" +
                "!مزاج بطاقات\n\n" +

                "❓ المساعدة:\n" +
                "!مزاج مساعدة\n\n" +

                "🎨 الفرق:\n" +
                "🔴 احمر\n" +
                "🔵 ازرق\n" +
                "🟡 اصفر\n" +
                "🟣 بنفسجي\n\n" +

                "🔢 الأرقام العربية والإنكليزية مدعومة."
            );
        }

        // ==============================
        // تحويل الأرقام العربية
        // ==============================

        private static string ToEnglishNumbers(
            string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

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

        // ==============================
        // إرسال الرد
        // ==============================

        private async Task SendReply(
            string text)
        {
            await Reply(text);
        }
    }
}
