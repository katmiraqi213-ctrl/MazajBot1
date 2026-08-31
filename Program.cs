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
            string email = Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";
            string password = Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("WOLF_EMAIL و WOLF_PASSWORD غير موجودين.");
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
                Console.WriteLine("بوت مزاج متصل بـ WOLF!");
                return Task.CompletedTask;
            };

            var result = await _client.Login(email, password);

            Console.WriteLine(result
                ? "تم تسجيل الدخول بنجاح!"
                : "فشل تسجيل الدخول - تأكد من بيانات الحساب");

            await Task.Delay(-1);
        }
    }

    public class MazajCard
    {
        public int Number { get; set; }
        public string Name { get; set; } = "";
        public int Points { get; set; }
        public bool Picked { get; set; }
    }

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

        public string CurrentTeam =>
            TeamNames.Count == 0 ? "" : TeamNames[CurrentTeamIndex];

        public int TotalJoined =>
            Teams.Values.Sum(t => t.Count);

        public void NextTurn()
        {
            if (TeamNames.Count > 0)
                CurrentTeamIndex =
                    (CurrentTeamIndex + 1) % TeamNames.Count;
        }
    }

    public class MazajGameCommands : WolfContext
    {
        private static readonly Dictionary<long, MazajGame> Games = new();
        private static readonly Random Random = new();

        private static readonly string[] AllTeams =
        {
            "احمر",
            "ازرق",
            "اصفر",
            "بنفسجي"
        };

        // الأسماء الخاصة التي أعطاها المستخدم
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

        [Command("مزاج")]
        public async Task HandleMazaj(string message)
        {
            long chatId = SourceId;
            string userName = SourceSubscriber?.Name ?? "لاعب";

            string[] parts = (message ?? "")
                .Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                await SendReply(
                    "أوامر مزاج:\n" +
                    "!مزاج جديد <النقاط> <عدد الفرق>\n" +
                    "!مزاج انضم <اللون>\n" +
                    "!مزاج تغيير <اللون>\n" +
                    "!مزاج لاعبين\n" +
                    "!مزاج بدء\n" +
                    "!مزاج اختار <رقم>\n" +
                    "!مزاج بطاقات\n" +
                    "!مزاج مساعدة"
                );
                return;
            }

            switch (parts[0])
            {
                case "جديد":
                    await CreateGame(chatId, parts);
                    break;

                case "انضم":
                case "تغيير":
                    await JoinTeam(chatId, parts, userName);
                    break;

                case "لاعبين":
                    await ShowPlayers(chatId);
                    break;

                case "بدء":
                    await StartGame(chatId);
                    break;

                case "اختار":
                    await PickCard(chatId, parts, userName);
                    break;

                case "بطاقات":
                    await ShowCards(chatId);
                    break;

                case "مساعدة":
                    await ShowHelp();
                    break;

                default:
                    await SendReply(
                        "الأمر غير معروف.\n" +
                        "اكتب !مزاج مساعدة لعرض الأوامر."
                    );
                    break;
            }
        }

        private async Task CreateGame(long chatId, string[] parts)
        {
            if (parts.Length < 3 ||
                !int.TryParse(ToEnglishNumbers(parts[1]), out int points) ||
                !int.TryParse(ToEnglishNumbers(parts[2]), out int teamCount))
            {
                await SendReply(
                    "الصيغة الصحيحة:\n" +
                    "!مزاج جديد <النقاط> <عدد الفرق>\n\n" +
                    "مثال:\n" +
                    "!مزاج جديد 400 4"
                );
                return;
            }

            if (points <= 0)
            {
                await SendReply("النقاط لازم تكون أكبر من صفر.");
                return;
            }

            if (teamCount < 2 || teamCount > 4)
            {
                await SendReply("عدد الفرق لازم يكون بين 2 و4.");
                return;
            }

            var game = new MazajGame
            {
                TargetPoints = points,
                TeamCount = teamCount,
                Started = false,
                CurrentTeamIndex = 0
            };

            for (int i = 0; i < teamCount; i++)
            {
                string team = AllTeams[i];

                game.TeamNames.Add(team);
                game.Teams[team] = new List<string>();
                game.Scores[team] = 0;
            }

            CreateCards(game);

            Games[chatId] = game;

            await SendReply(
                $"🎭 لعبة مزاج جديدة!\n\n" +
                $"🎯 النقاط: {points}\n" +
                $"👥 الفرق: {string.Join(" - ", game.TeamNames)}\n" +
                $"🃏 عدد البطاقات: 65\n\n" +
                "للانضمام:\n" +
                "!مزاج انضم <اللون>"
            );
        }

        private static void CreateCards(MazajGame game)
        {
            game.Cards.Clear();

            var names = new List<string>();

            // البطاقات الخاصة
            foreach (var card in SpecialCards)
                names.Add(card.Key);

            // إكمال العدد إلى 65 بطاقة
            int normalNumber = 1;

            while (names.Count < 65)
            {
                string name;

                do
                {
                    name = Random.Next(0, 2) == 0
                        ? $"ضربة عشوائية {normalNumber}"
                        : $"بطاقة حظ {normalNumber}";

                    normalNumber++;
                }
                while (names.Contains(name));

                names.Add(name);
            }

            // خلط أسماء البطاقات
            names = names
                .OrderBy(_ => Random.Next())
                .ToList();

            for (int i = 0; i < 65; i++)
            {
                string name = names[i];

                int value;

                if (SpecialCards.TryGetValue(name, out int specialValue))
                {
                    value = specialValue;
                }
                else
                {
                    // البطاقات العشوائية موجبة أو سالبة
                    int[] values =
                    {
                        -100, -75, -50, -25,
                        25, 50, 75, 100
                    };

                    value = values[Random.Next(values.Length)];
                }

                game.Cards.Add(new MazajCard
                {
                    Number = i + 1,
                    Name = name,
                    Points = value,
                    Picked = false
                });
            }
        }

        private async Task JoinTeam(
            long chatId,
            string[] parts,
            string userName)
        {
            if (!Games.TryGetValue(chatId, out var game))
            {
                await SendReply(
                    "ماكو لعبة شغالة حالياً.\n" +
                    "اكتب !مزاج جديد 400 4"
                );
                return;
            }

            if (game.Started)
            {
                await SendReply(
                    "اللعبة بدت، ما تكدر تنضم أو تغير الفريق هسه."
                );
                return;
            }

            if (parts.Length < 2)
            {
                await SendReply(
                    $"حدد اللون:\n{string.Join(" - ", game.TeamNames)}"
                );
                return;
            }

            string team = parts[1];

            if (!game.Teams.ContainsKey(team))
            {
                await SendReply(
                    $"هذا الفريق غير موجود.\n" +
                    $"المتاح: {string.Join(" - ", game.TeamNames)}"
                );
                return;
            }

            foreach (var players in game.Teams.Values)
                players.Remove(userName);

            if (!game.Teams[team].Contains(userName))
                game.Teams[team].Add(userName);

            await SendReply(
                $"✅ {userName} انضم إلى فريق {team}."
            );
        }

        private async Task ShowPlayers(long chatId)
        {
            if (!Games.TryGetValue(chatId, out var game))
            {
                await SendReply("ماكو لعبة شغالة حالياً.");
                return;
            }

            string result =
                $"👥 اللاعبين ({game.TotalJoined})\n\n";

            foreach (string team in game.TeamNames)
            {
                var players = game.Teams[team];

                result +=
                    $"🔹 {team} ({players.Count}): " +
                    (players.Count == 0
                        ? "لا يوجد"
                        : string.Join("، ", players)) +
                    "\n";
            }

            await SendReply(result);
        }

        private async Task StartGame(long chatId)
        {
            if (!Games.TryGetValue(chatId, out var game))
            {
                await SendReply("ماكو لعبة شغالة حالياً.");
                return;
            }

            var activeTeams = game.TeamNames
                .Where(t => game.Teams[t].Count > 0)
                .ToList();

            if (activeTeams.Count < 2)
            {
                await SendReply(
                    "لازم يكون أكو لاعبين بفريقين على الأقل."
                );
                return;
            }

            game.TeamNames = activeTeams;
            game.Started = true;
            game.CurrentTeamIndex = 0;

            await SendReply(
                "🔥 اللعبة بدأت!\n\n" +
                $"🎯 الدور الآن على فريق: {game.CurrentTeam}\n\n" +
                "اختاروا بطاقة:\n" +
                "!مزاج اختار <رقم>\n\n" +
                "مثال: !مزاج اختار 13\n" +
                "أو: !مزاج اختار ١٣"
            );
        }

        private async Task PickCard(
            long chatId,
            string[] parts,
            string userName)
        {
            if (!Games.TryGetValue(chatId, out var game))
            {
                await SendReply("ماكو لعبة شغالة حالياً.");
                return;
            }

            if (!game.Started)
            {
                await SendReply(
                    "اللعبة لسه ما بدت.\n" +
                    "اكتب !مزاج بدء"
                );
                return;
            }

            string playerTeam =
                game.Teams
                    .FirstOrDefault(t => t.Value.Contains(userName))
                    .Key;

            if (string.IsNullOrEmpty(playerTeam))
            {
                await SendReply(
                    "أنت مو منضم لأي فريق."
                );
                return;
            }

            if (playerTeam != game.CurrentTeam)
            {
                await SendReply(
                    $"⏳ استنى دورك!\n" +
                    $"الدور الآن على فريق {game.CurrentTeam}."
                );
                return;
            }

            if (parts.Length < 2)
            {
                await SendReply(
                    "اكتب رقم البطاقة.\n" +
                    "مثال: !مزاج اختار 13"
                );
                return;
            }

            string numberText = ToEnglishNumbers(parts[1]);

            if (!int.TryParse(numberText, out int cardNumber))
            {
                await SendReply(
                    "رقم البطاقة غير صحيح."
                );
                return;
            }

            if (cardNumber < 1 || cardNumber > 65)
            {
                await SendReply(
                    "رقم البطاقة لازم يكون بين 1 و65."
                );
                return;
            }

            MazajCard? card =
                game.Cards.FirstOrDefault(c =>
                    c.Number == cardNumber);

            if (card == null)
            {
                await SendReply("البطاقة غير موجودة.");
                return;
            }

            if (card.Picked)
            {
                await SendReply(
                    "❌ هذي البطاقة مأخوذة، اختار بطاقة ثانية."
                );
                return;
            }

            card.Picked = true;

            game.Scores[playerTeam] += card.Points;

            string pointsText =
                card.Points >= 0
                    ? $"+{card.Points}"
                    : card.Points.ToString();

            await SendReply(
                $"🃏 تم اختيار البطاقة {card.Number}\n\n" +
                $"🎭 {card.Name}\n" +
                $"💰 النقاط: {pointsText}\n\n" +
                $"🔵 فريق {playerTeam}: " +
                $"{game.Scores[playerTeam]} نقطة"
            );

            if (game.Cards.All(c => c.Picked))
            {
                await EndGame(chatId);
                return;
            }

            game.NextTurn();

            await SendReply(
                $"🔄 الدور الآن على فريق: {game.CurrentTeam}"
            );
        }

        private async Task ShowCards(long chatId)
        {
            if (!Games.TryGetValue(chatId, out var game))
            {
                await SendReply("ماكو لعبة شغالة حالياً.");
                return;
            }

            string result = "🃏 بطاقات مزاج\n\n";

            foreach (var card in game.Cards)
            {
                result += card.Picked
                    ? $"❌ {card.Number}\n"
                    : $"🟢 {card.Number}\n";
            }

            if (game.Started)
            {
                result +=
                    $"\n🎯 الدور: {game.CurrentTeam}";
            }

            await SendReply(result);
        }

        private async Task EndGame(long chatId)
        {
            if (!Games.TryGetValue(chatId, out var game))
                return;

            var standings =
                game.Scores
                    .OrderByDescending(x => x.Value)
                    .ToList();

            string winner = standings[0].Key;

            string scores = string.Join(
                "\n",
                standings.Select(x =>
                    $"🏆 {x.Key}: {x.Value} نقطة")
            );

            await SendReply(
                "🏁 انتهت لعبة مزاج!\n\n" +
                scores +
                $"\n\n🥇 الفائز: {winner}"
            );

            Games.Remove(chatId);
        }

        private async Task ShowHelp()
        {
            await SendReply(
                "🎭 أوامر بوت مزاج\n\n" +

                "!مزاج جديد 400 4\n" +
                "إنشاء لعبة بـ400 نقطة و4 فرق\n\n" +

                "!مزاج انضم احمر\n" +
                "الانضمام إلى فريق\n\n" +

                "!مزاج تغيير ازرق\n" +
                "تغيير الفريق\n\n" +

                "!مزاج لاعبين\n" +
                "عرض اللاعبين\n\n" +

                "!مزاج بدء\n" +
                "بدء اللعبة\n\n" +

                "!مزاج بطاقات\n" +
                "عرض البطاقات المتاحة\n\n" +

                "!مزاج اختار 13\n" +
                "!مزاج اختار ١٣\n" +
                "اختيار بطاقة\n\n" +

                "الفرق:\n" +
                "🔴 احمر\n" +
                "🔵 ازرق\n" +
                "🟡 اصفر\n" +
                "🟣 بنفسجي"
            );
        }

        private static string ToEnglishNumbers(string text)
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

        private async Task SendReply(string text)
        {
            await Reply(text);
        }
    }
}
