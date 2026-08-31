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
        private static IWolfClient _client;

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
                Console.WriteLine("Bot connected to WOLF!");
                return Task.CompletedTask;
            };

            var result = await _client.Login(email, password);

            Console.WriteLine(
                result
                    ? "Login succeeded!"
                    : "Login failed - check email and password"
            );

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

        public List<string> TeamNames = new List<string>();

        public Dictionary<string, List<string>> Teams =
            new Dictionary<string, List<string>>();

        public Dictionary<string, int> Scores =
            new Dictionary<string, int>();

        public List<MazajCard> Cards =
            new List<MazajCard>();

        public bool Started = false;

        public int CurrentTeamIndex = 0;

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
                (CurrentTeamIndex + 1) % TeamNames.Count;
        }
    }

    public class MazajGameCommands : WolfContext
    {
        private static readonly Dictionary<long, MazajGame> Games =
            new Dictionary<long, MazajGame>();

        private static readonly Random Random =
            new Random();

        private static readonly string[] AllColors =
        {
            "احمر",
            "ازرق",
            "اصفر",
            "بنفسجي"
        };

        // الأسماء الخاصة التي طلبتها
        private static readonly Dictionary<string, int> SpecialCards =
            new Dictionary<string, int>
            {
                { "ضربة الوحش محمد 🇮🇶❤️", 100 },

                { "هولو وئام الفگر", -100 },

                { "طاحج حضج توت 😂", -50 },
                { "صخام بوجهك ايهاب", -50 },
                { "سراوي تيتي لاتحل ولا تربط", -50 },
                { "هذا حظ زوز", -50 },
                { "لولو التعبانه", -50 },
                { "نواره السلبيه", -50 },

                { "ضربة ابو حامد", -50 },
                { "ضربة حمدي الوزير", -50 },
                { "ضربة حيدر بنكه", -50 },
                { "ضربة جمو موسيقى", -50 },

                { "ضربة اساور صاروخ باليستي", 75 },
                { "صاروخ ارض ارض", 75 },
                { "ضربة علي القويه", 75 },
                { "ضربة ابو جنه", 75 }
            };

        [Command("مزاج")]
        public async Task HandleMazaj(string message)
        {
            long chatId = this.SourceId;

            string userName =
                this.SourceSubscriber?.Name ?? "لاعب";

            var parts = (message ?? "")
                .Trim()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );

            if (parts.Length == 0)
            {
                await SendReply(
                    "اكتب أمر بعد !مزاج\n\n" +
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

            string action = parts[0];

            switch (action)
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
                        "أمر غير معروف.\n" +
                        "اكتب !مزاج مساعدة"
                    );
                    break;
            }
        }

        private async Task CreateGame(
            long chatId,
            string[] parts)
        {
            if (parts.Length < 3)
            {
                await SendReply(
                    "الصيغة الصحيحة:\n" +
                    "!مزاج جديد <النقاط> <عدد الفرق>\n\n" +
                    "مثال:\n" +
                    "!مزاج جديد 400 4"
                );

                return;
            }

            if (!TryParseNumber(parts[1], out int points))
            {
                await SendReply(
                    "النقاط لازم تكون رقم."
                );

                return;
            }

            if (!TryParseNumber(parts[2], out int teamCount))
            {
                await SendReply(
                    "عدد الفرق لازم يكون رقم."
                );

                return;
            }

            if (points <= 0)
            {
                await SendReply(
                    "عدد النقاط لازم يكون أكبر من صفر."
                );

                return;
            }

            if (teamCount < 2 || teamCount > 4)
            {
                await SendReply(
                    "عدد الفرق لازم يكون بين 2 و4."
                );

                return;
            }

            var game = new MazajGame
            {
                TargetPoints = points,
                TeamCount = teamCount
            };

            for (int i = 0; i < teamCount; i++)
            {
                string color = AllColors[i];

                game.TeamNames.Add(color);
                game.Teams[color] =
                    new List<string>();

                game.Scores[color] = 0;
            }

            CreateCards(game);

            Games[chatId] = game;

            string teams =
                string.Join(
                    " - ",
                    game.TeamNames);

            await SendReply(
                "🎭🔥 لعبة مزاج جديدة 🔥🎭\n\n" +
                $"🎯 النقاط المطلوبة: {points}\n" +
                $"👥 الفرق: {teams}\n" +
                "🃏 عدد البطاقات: 65\n\n" +
                "للانضمام:\n" +
                "!مزاج انضم <اسم اللون>\n\n" +
                "مثال:\n" +
                "!مزاج انضم احمر"
            );
        }

        private void CreateCards(MazajGame game)
        {
            game.Cards.Clear();

            var specialCards =
                SpecialCards
                    .OrderBy(x => Random.Next())
                    .ToList();

            int number = 1;

            foreach (var special in specialCards)
            {
                game.Cards.Add(
                    new MazajCard
                    {
                        Number = number,
                        Name = special.Key,
                        Points = special.Value,
                        Picked = false
                    });

                number++;
            }

            string[] normalPositiveNames =
            {
                "ضربة النجمة",
                "الصاروخ السريع",
                "ضربة الأبطال",
                "الطلقة الذهبية",
                "قوة الفريق",
                "الضربة الملكية",
                "الحظ الجميل",
                "ضربة الصقر",
                "النصر الكبير",
                "ضربة البرق"
            };

            string[] normalNegativeNames =
            {
                "ضربة الحظ السيئ",
                "تعطيل الحظ",
                "ضربة المفاجأة",
                "خصم مفاجئ",
                "نحس البطاقة",
                "الضربة الباردة",
                "خسارة صغيرة",
                "خصم الحظ",
                "ضربة النحس",
                "العثرة"
            };

            while (game.Cards.Count < 65)
            {
                bool positive =
                    Random.Next(0, 2) == 0;

                int value;

                if (positive)
                {
                    value = Random.Next(10, 101);
                }
                else
                {
                    value = -Random.Next(10, 101);
                }

                string name;

                if (positive)
                {
                    name =
                        normalPositiveNames[
                            Random.Next(
                                normalPositiveNames.Length)];
                }
                else
                {
                    name =
                        normalNegativeNames[
                            Random.Next(
                                normalNegativeNames.Length)];
                }

                game.Cards.Add(
                    new MazajCard
                    {
                        Number = number,
                        Name = name,
                        Points = value,
                        Picked = false
                    });

                number++;
            }

            // خلط البطاقات حتى لا تكون الأسماء المهمة
            // بأرقام ثابتة
            game.Cards =
                game.Cards
                    .OrderBy(x => Random.Next())
                    .ToList();

            // إعادة ترقيم البطاقات من 1 إلى 65
            for (int i = 0; i < game.Cards.Count; i++)
            {
                game.Cards[i].Number = i + 1;
            }
        }

        private async Task JoinTeam(
            long chatId,
            string[] parts,
            string userName)
        {
            if (!Games.ContainsKey(chatId))
            {
                await SendReply(
                    "ماكو لعبة شغالة حالياً.\n" +
                    "اكتب !مزاج جديد"
                );

                return;
            }

            var game = Games[chatId];

            if (game.Started)
            {
                await SendReply(
                    "اللعبة بدت، ما تكدر تنضم أو تغير الفريق."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await SendReply(
                    "حدد اللون:\n" +
                    string.Join(
                        " - ",
                        game.TeamNames)
                );

                return;
            }

            string team = parts[1];

            if (!game.Teams.ContainsKey(team))
            {
                await SendReply(
                    "هذا اللون غير موجود.\n" +
                    "الألوان:\n" +
                    string.Join(
                        " - ",
                        game.TeamNames)
                );

                return;
            }

            foreach (var players in game.Teams.Values)
            {
                players.Remove(userName);
            }

            game.Teams[team].Add(userName);

            await SendReply(
                $"👤 {userName}\n" +
                $"انضم لفريق {team} ✅"
            );
        }

        private async Task ShowPlayers(long chatId)
        {
            if (!Games.ContainsKey(chatId))
            {
                await SendReply(
                    "ماكو لعبة شغالة حالياً."
                );

                return;
            }

            var game = Games[chatId];

            string result =
                $"👥 اللاعبين ({game.TotalJoined})\n\n";

            foreach (var team in game.Teams)
            {
                string players =
                    team.Value.Count > 0
                        ? string.Join(
                            "، ",
                            team.Value)
                        : "لا يوجد";

                result +=
                    $"🔴 {team.Key} " +
                    $"({team.Value.Count}): " +
                    $"{players}\n";
            }

            await SendReply(result);
        }

        private async Task StartGame(long chatId)
        {
            if (!Games.ContainsKey(chatId))
            {
                await SendReply(
                    "ماكو لعبة شغالة حالياً."
                );

                return;
            }

            var game = Games[chatId];

            if (game.Started)
            {
                await SendReply(
                    "اللعبة بدأت مسبقاً."
                );

                return;
            }

            var teamsWithPlayers =
                game.Teams
                    .Where(x => x.Value.Count > 0)
                    .Select(x => x.Key)
                    .ToList();

            if (teamsWithPlayers.Count < 2)
            {
                await SendReply(
                    "لازم يكون عندنا لاعبين بفريقين على الأقل."
                );

                return;
            }

            game.TeamNames =
                teamsWithPlayers;

            game.Started = true;
            game.CurrentTeamIndex = 0;

            await SendReply(
                "🎭🔥 اللعبة بدأت 🔥🎭\n\n" +
                $"الدور الآن على فريق: {game.CurrentTeam}\n\n" +
                "اختاروا بطاقة:\n" +
                "!مزاج اختار <رقم>\n\n" +
                "مثال:\n" +
                "!مزاج اختار 13"
            );
        }

        private async Task PickCard(
            long chatId,
            string[] parts,
            string userName)
        {
            if (!Games.ContainsKey(chatId))
            {
                await SendReply(
                    "ماكو لعبة شغالة حالياً."
                );

                return;
            }

            var game = Games[chatId];

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
                    .FirstOrDefault(
                        x => x.Value.Contains(userName))
                    .Key;

            if (string.IsNullOrEmpty(playerTeam))
            {
                await SendReply(
                    "انت مو منضم لأي فريق."
                );

                return;
            }

            if (playerTeam != game.CurrentTeam)
            {
                await SendReply(
                    $"⏳ استنى دورك!\n" +
                    $"الدور الآن على فريق {game.CurrentTeam}"
                );

                return;
            }

            if (parts.Length < 2)
            {
                await SendReply(
                    "حدد رقم البطاقة.\n" +
                    "مثال: !مزاج اختار 13"
                );

                return;
            }

            if (!TryParseNumber(
                    parts[1],
                    out int cardNumber))
            {
                await SendReply(
                    "رقم البطاقة غير صحيح."
                );

                return;
            }

            if (cardNumber < 1 ||
                cardNumber > game.Cards.Count)
            {
                await SendReply(
                    $"رقم البطاقة لازم يكون بين 1 و {game.Cards.Count}."
                );

                return;
            }

            var card =
                game.Cards
                    .First(x => x.Number == cardNumber);

            if (card.Picked)
            {
                await SendReply(
                    "❌ هذه البطاقة مأخوذة، اختار بطاقة ثانية."
                );

                return;
            }

            card.Picked = true;

            game.Scores[playerTeam] +=
                card.Points;

            string pointsText =
                card.Points >= 0
                    ? $"+{card.Points}"
                    : card.Points.ToString();

            await SendReply(
                $"🃏 البطاقة رقم {card.Number}\n\n" +
                $"🎭 {card.Name}\n" +
                $"💰 النقاط: {pointsText}\n\n" +
                $"فريق {playerTeam}: " +
                $"{game.Scores[playerTeam]} نقطة"
            );

            // الفوز إذا وصل فريق للنقاط المطلوبة
            if (game.Scores[playerTeam] >=
                game.TargetPoints)
            {
                await EndGame(
                    chatId,
                    $"وصل فريق {playerTeam} إلى " +
                    $"{game.TargetPoints} نقطة.");

                return;
            }

            if (game.RemainingCards == 0)
            {
                await EndGame(
                    chatId,
                    "خلصت جميع البطاقات.");

                return;
            }

            game.NextTurn();

            await SendReply(
                $"🔄 الدور الآن على فريق: " +
                $"{game.CurrentTeam}"
            );
        }

        private async Task ShowCards(long chatId)
        {
            if (!Games.ContainsKey(chatId))
            {
                await SendReply(
                    "ماكو لعبة شغالة حالياً."
                );

                return;
            }

            var game = Games[chatId];

            string result =
                "🃏 بطاقات مزاج\n\n";

            foreach (var card in game.Cards)
            {
                result +=
                    card.Picked
                        ? $"{card.Number}: ✕\n"
                        : $"{card.Number}: 🃏\n";
            }

            result +=
                $"\nالمتبقي: {game.RemainingCards}";

            if (game.Started)
            {
                result +=
                    $"\nالدور: {game.CurrentTeam}";
            }

            await SendReply(result);
        }

        private async Task EndGame(
            long chatId,
            string reason)
        {
            var game = Games[chatId];

            var sorted =
                game.Scores
                    .OrderByDescending(x => x.Value)
                    .ToList();

            string winner =
                sorted.First().Key;

            string standings =
                string.Join(
                    "\n",
                    sorted.Select(
                        x =>
                            $"{x.Key}: {x.Value} نقطة"));

            await SendReply(
                "🏆🎭 انتهت لعبة مزاج 🎭🏆\n\n" +
                $"{reason}\n\n" +
                "📊 النتائج:\n" +
                $"{standings}\n\n" +
                $"👑 الفريق الفائز: {winner}"
            );

            Games.Remove(chatId);
        }

        private async Task ShowHelp()
        {
            await SendReply(
                "🎭 أوامر بوت مزاج 🎭\n\n" +

                "🆕 إنشاء لعبة:\n" +
                "!مزاج جديد <النقاط> <الفرق>\n\n" +

                "👥 الانضمام:\n" +
                "!مزاج انضم <اللون>\n\n" +

                "🔄 تغيير الفريق:\n" +
                "!مزاج تغيير <اللون>\n\n" +

                "👤 اللاعبين:\n" +
                "!مزاج لاعبين\n\n" +

                "▶️ بدء اللعبة:\n" +
                "!مزاج بدء\n\n" +

                "🃏 اختيار بطاقة:\n" +
                "!مزاج اختار <رقم>\n\n" +

                "📋 عرض البطاقات:\n" +
                "!مزاج بطاقات\n\n" +

                "❓ المساعدة:\n" +
                "!مزاج مساعدة\n\n" +

                "🎨 الألوان:\n" +
                "احمر - ازرق - اصفر - بنفسجي\n\n" +

                "🔢 يقبل البوت الأرقام العربية والإنكليزية."
            );
        }

        // يحول:
        // ١٢٣٤٥٦٧٨٩٠
        // إلى:
        // 1234567890
        private static string NormalizeArabicNumbers(
            string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return input
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

        private static bool TryParseNumber(
            string input,
            out int number)
        {
            string normalized =
                NormalizeArabicNumbers(
                    input.Trim());

            return int.TryParse(
                normalized,
                out number);
        }

        private async Task SendReply(string text)
        {
            await this.Reply(text);
        }
    }
}
