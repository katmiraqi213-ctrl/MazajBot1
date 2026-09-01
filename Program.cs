using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Commands;
using WolfLive.Api.Models;

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
                "خطأ: WOLF_EMAIL أو WOLF_PASSWORD غير موجود."
            );

            return;
        }

        _client = new WolfClient()
            .SetupCommands()
            .WithCommandSet(c =>
            {
                c.AddCommands<MazajCommands>()
                 .WithPrefix("!");
            })
            .WithSerilog()
            .Done();

        _client.OnConnected += (_) =>
        {
            Console.WriteLine("WOLF BOT CONNECTED!");
        };

        var result = await _client.Login(email, password);

        Console.WriteLine(
            result
                ? "LOGIN SUCCESS!"
                : "LOGIN FAILED!"
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
    public int TargetPoints { get; set; }

    public int TeamCount { get; set; }

    public List<string> TeamNames { get; set; } = new();

    public Dictionary<string, List<string>> Teams { get; set; } = new();

    public Dictionary<string, int> Scores { get; set; } = new();

    public List<MazajCard> Cards { get; set; } = new();

    public bool Started { get; set; }

    public int CurrentTeamIndex { get; set; }

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

    public int RemainingCards =>
        Cards.Count(x => !x.Picked);

    public int TotalJoined =>
        Teams.Values.Sum(x => x.Count);

    public void NextTurn()
    {
        if (TeamNames.Count == 0)
            return;

        CurrentTeamIndex =
            (CurrentTeamIndex + 1) % TeamNames.Count;
    }
}

public class MazajCommands : WolfContext
{
    private static readonly Dictionary<string, MazajGame> Games = new();

    private static readonly Random Random = new();

    private static readonly string[] AllTeams =
    {
        "احمر",
        "ازرق",
        "اصفر",
        "بنفسجي"
    };

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

            { "ضربة ابو عماد", -75 },

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
        long chatId = GetChatId();

        string userName =
            User?.Name ?? "لاعب";

        string[] parts =
            (message ?? "")
                .Trim()
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );

        if (parts.Length == 0)
        {
            await ShowHelp();
            return;
        }

        switch (parts[0])
        {
            case "جديد":
                await CreateGame(chatId, parts);
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
                await Reply(
                    "❌ الأمر غير معروف.\n" +
                    "اكتب !مزاج مساعدة"
                );
                break;
        }
    }

    private long GetChatId()
    {
        if (Message == null)
            return 0;

        if (Message.IsGroup)
        {
            if (long.TryParse(
                Message.GroupId,
                out long groupId))
            {
                return groupId;
            }
        }

        if (long.TryParse(
            Message.UserId,
            out long userId))
        {
            return userId;
        }

        return Message.IsGroup
            ? Message.GroupId.GetHashCode()
            : Message.UserId.GetHashCode();
    }

    private async Task CreateGame(
        long chatId,
        string[] parts)
    {
        if (parts.Length < 3)
        {
            await Reply(
                "الصيغة الصحيحة:\n\n" +
                "!مزاج جديد 400 4"
            );

            return;
        }

        if (!TryParseNumber(
            parts[1],
            out int points))
        {
            await Reply(
                "❌ النقاط لازم تكون رقم."
            );

            return;
        }

        if (!TryParseNumber(
            parts[2],
            out int teamCount))
        {
            await Reply(
                "❌ عدد الفرق لازم يكون رقم."
            );

            return;
        }

        if (points <= 0)
        {
            await Reply(
                "❌ النقاط لازم تكون أكبر من صفر."
            );

            return;
        }

        if (teamCount < 2 ||
            teamCount > 4)
        {
            await Reply(
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
            string team = AllTeams[i];

            game.TeamNames.Add(team);

            game.Teams[team] =
                new List<string>();

            game.Scores[team] = 0;
        }

        CreateCards(game);

        Games[chatId] = game;

        await Reply(
            "🎭🔥 لعبة مزاج جديدة 🔥🎭\n\n" +
            $"🎯 النقاط المطلوبة: {points}\n" +
            $"👥 الفرق: {string.Join(" - ", game.TeamNames)}\n" +
            "🃏 عدد البطاقات: 65\n\n" +
            "للانضمام:\n" +
            "!مزاج انضم احمر\n" +
            "!مزاج انضم ازرق\n" +
            "!مزاج انضم اصفر\n" +
            "!مزاج انضم بنفسجي\n\n" +
            "بعد اكتمال الفرق:\n" +
            "!مزاج بدء"
        );
    }

    private static void CreateCards(
        MazajGame game)
    {
        game.Cards.Clear();

        foreach (var special in SpecialCards)
        {
            game.Cards.Add(
                new MazajCard
                {
                    Name = special.Key,
                    Points = special.Value,
                    Picked = false
                }
            );
        }

        // إضافة 30 بطاقة سند سوريا
        for (int i = 1; i <= 30; i++)
        {
            game.Cards.Add(
                new MazajCard
                {
                    Name = $"ضربة سند سوريا {i}",
                    Points = 30,
                    Picked = false
                }
            );
        }

        string[] positive =
        {
            "ضربة النجمة",
            "الصاروخ السريع",
            "ضربة الأبطال",
            "الطلقة الذهبية",
            "قوة الفريق",
            "الضربة الملكية",
            "الحظ الجميل",
            "ضربة الصقر"
        };

        string[] negative =
        {
            "ضربة الحظ السيئ",
            "تعطيل الحظ",
            "ضربة المفاجأة",
            "خصم مفاجئ",
            "نحس البطاقة",
            "الضربة الباردة",
            "خسارة صغيرة",
            "ضربة النحس"
        };

        int normalNumber = 1;

        while (game.Cards.Count < 65)
        {
            bool isPositive =
                Random.Next(0, 2) == 0;

            int[] values =
                isPositive
                    ? new[] { 25, 50, 75, 100 }
                    : new[] { -25, -50, -75, -100 };

            int points =
                values[
                    Random.Next(values.Length)
                ];

            string name;

            if (isPositive)
            {
                name =
                    positive[
                        Random.Next(
                            positive.Length)
                    ];
            }
            else
            {
                name =
                    negative[
                        Random.Next(
                            negative.Length)
                    ];
            }

            name += $" {normalNumber}";

            normalNumber++;

            game.Cards.Add(
                new MazajCard
                {
                    Name = name,
                    Points = points,
                    Picked = false
                }
            );
        }

        game.Cards =
            game.Cards
                .OrderBy(_ => Random.Next())
                .ToList();

        for (int i = 0;
             i < game.Cards.Count;
             i++)
        {
            game.Cards[i].Number = i + 1;
        }
    }

    private async Task JoinTeam(
        long chatId,
        string[] parts,
        string userName)
    {
        if (!Games.TryGetValue(
            chatId,
            out var game))
        {
            await Reply(
                "❌ ماكو لعبة شغالة.\n\n" +
                "اكتب:\n" +
                "!مزاج جديد 400 4"
            );

            return;
        }

        if (game.Started)
        {
            await Reply(
                "❌ اللعبة بدأت، ما تكدر تغير الفريق."
            );

            return;
        }

        if (parts.Length < 2)
        {
            await Reply(
                "حدد الفريق:\n" +
                string.Join(
                    " - ",
                    game.TeamNames)
            );

            return;
        }

        string team = parts[1];

        if (!game.Teams.ContainsKey(team))
        {
            await Reply(
                "❌ هذا الفريق غير موجود."
            );

            return;
        }

        foreach (var players
                 in game.Teams.Values)
        {
            players.Remove(userName);
        }

        game.Teams[team].Add(userName);

        await Reply(
            $"✅ {userName}\n" +
            $"انضم إلى فريق {team}"
        );
    }

    private async Task ShowPlayers(
        long chatId)
    {
        if (!Games.TryGetValue(
            chatId,
            out var game))
        {
            await Reply(
                "❌ ماكو لعبة شغالة."
            );

            return;
        }

        string result =
            $"👥 اللاعبين ({game.TotalJoined})\n\n";

        foreach (string team
                 in game.TeamNames)
        {
            var players =
                game.Teams[team];

            result +=
                $"🔹 {team} ({players.Count})\n";

            if (players.Count == 0)
            {
                result += "لا يوجد لاعبين\n";
            }
            else
            {
                foreach (var player
                         in players)
                {
                    result +=
                        $"• {player}\n";
                }
            }

            result += "\n";
        }

        await Reply(result);
    }

    private async Task StartGame(
        long chatId)
    {
        if (!Games.TryGetValue(
            chatId,
            out var game))
        {
            await Reply(
                "❌ ماكو لعبة شغالة."
            );

            return;
        }

        if (game.Started)
        {
            await Reply(
                "❌ اللعبة بدأت مسبقًا."
            );

            return;
        }

        var activeTeams =
            game.TeamNames
                .Where(
                    x =>
                        game.Teams[x].Count > 0)
                .ToList();

        if (activeTeams.Count < 2)
        {
            await Reply(
                "❌ لازم يكون أكو لاعبين بفريقين على الأقل."
            );

            return;
        }

        game.TeamNames =
            activeTeams;

        game.Started = true;

        game.CurrentTeamIndex = 0;

        await Reply(
            "🎭🔥 بدأت لعبة مزاج 🔥🎭\n\n" +
            $"🎯 الدور على فريق: {game.CurrentTeam}\n\n" +
            "🃏 اختار البطاقة بكتابة الرقم فقط.\n\n" +
            "مثال:\n" +
            "13\n" +
            "أو\n" +
            "١٣"
        );

        await NumberGameLoop(chatId, game);
    }

    private async Task NumberGameLoop(
        long chatId,
        MazajGame game)
    {
        while (
            Games.TryGetValue(
                chatId,
                out var currentGame) &&
            currentGame.Started)
        {
            Message msg;

            try
            {
                msg =
                    await Client.NextMessage(
                        m =>
                            IsSameChat(
                                m,
                                chatId) &&
                            IsNumber(
                                m.Content)
                    );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "NextMessage error: " +
                    ex.Message
                );

                return;
            }

            string numberText =
                ToEnglishNumbers(
                    msg.Content.Trim());

            if (!int.TryParse(
                numberText,
                out int cardNumber))
            {
                continue;
            }

            if (cardNumber < 1 ||
                cardNumber > 65)
            {
                continue;
            }

            string userName =
                await GetUserName(msg);

            string playerTeam =
                FindPlayerTeam(
                    game,
                    userName);

            if (string.IsNullOrEmpty(
                playerTeam))
            {
                await Client.Reply(
                    msg,
                    "❌ أنت مو منضم لأي فريق."
                );

                continue;
            }

            if (playerTeam !=
                game.CurrentTeam)
            {
                await Client.Reply(
                    msg,
                    $"⏳ مو دور فريقك.\n" +
                    $"الدور الآن على فريق {game.CurrentTeam}"
                );

                continue;
            }

            MazajCard? card =
                game.Cards.FirstOrDefault(
                    x =>
                        x.Number ==
                        cardNumber);

            if (card == null)
                continue;

            if (card.Picked)
            {
                await Client.Reply(
                    msg,
                    "❌ هذه البطاقة مأخوذة، اختار رقم ثاني."
                );

                continue;
            }

            card.Picked = true;

            game.Scores[playerTeam] +=
                card.Points;

            string pointsText =
                card.Points >= 0
                    ? $"+{card.Points}"
                    : card.Points.ToString();

            await Client.Reply(
                msg,
                "🃏 بطاقة مزاج\n\n" +
                $"🔢 الرقم: {card.Number}\n" +
                $"🎭 {card.Name}\n" +
                $"💰 النقاط: {pointsText}\n\n" +
                $"🏆 فريق {playerTeam}: " +
                $"{game.Scores[playerTeam]} نقطة"
            );

            if (
                game.Scores[playerTeam] >=
                game.TargetPoints)
            {
                await EndGame(
                    chatId,
                    game,
                    playerTeam);

                return;
            }

            if (
                game.RemainingCards == 0)
            {
                await EndGame(
                    chatId,
                    game,
                    null);

                return;
            }

            game.NextTurn();

            await Client.Reply(
                msg,
                $"🔄 الدور الآن على فريق: " +
                $"{game.CurrentTeam}"
            );
        }
    }

    private static bool IsSameChat(
        Message msg,
        long chatId)
    {
        if (msg == null)
            return false;

        if (msg.IsGroup)
        {
            return long.TryParse(
                msg.GroupId,
                out long groupId) &&
                groupId == chatId;
        }

        return long.TryParse(
            msg.UserId,
            out long userId) &&
            userId == chatId;
    }

    private static bool IsNumber(
        string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string normalized =
            ToEnglishNumbers(
                text.Trim());

        return int.TryParse(
            normalized,
            out _);
    }

    private async Task<string> GetUserName(
        Message msg)
    {
        try
        {
            if (msg.IsGroup)
            {
                var groupUser =
                    await Client.GetGroupUser(
                        msg.GroupId,
                        msg.UserId);

                return groupUser?.User?.Name ??
                       "لاعب";
            }

            var user =
                await Client.GetUser(
                    msg.UserId);

            return user?.Name ??
                   "لاعب";
        }
        catch
        {
            return "لاعب";
        }
    }

    private static string FindPlayerTeam(
        MazajGame game,
        string userName)
    {
        foreach (var team
                 in game.Teams)
        {
            if (team.Value.Contains(
                userName))
            {
                return team.Key;
            }
        }

        return "";
    }

    private async Task ShowCards(
        long chatId)
    {
        if (!Games.TryGetValue(
            chatId,
            out var game))
        {
            await Reply(
                "❌ ماكو لعبة شغالة."
            );

            return;
        }

        string result =
            "🃏 بطاقات مزاج\n\n";

        foreach (var card
                 in game.Cards)
        {
            result +=
                card.Picked
                    ? $"❌ {card.Number}\n"
                    : $"🟢 {card.Number}\n";
        }

        result +=
            $"\nالمتبقي: {game.RemainingCards}";

        if (game.Started)
        {
            result +=
                $"\n🎯 الدور: {game.CurrentTeam}";
        }

        await Reply(result);
    }

    private async Task EndGame(
        long chatId,
        MazajGame game,
        string? directWinner)
    {
        var standings =
            game.Scores
                .OrderByDescending(
                    x => x.Value)
                .ToList();

        string winner =
            directWinner ??
            standings.First().Key;

        string scores =
            string.Join(
                "\n",
                standings.Select(
                    x =>
                        $"🏆 {x.Key}: " +
                        $"{x.Value} نقطة"
                )
            );

        await Reply(
            "🏁🎭 انتهت لعبة مزاج 🎭🏁\n\n" +
            scores +
            "\n\n" +
            $"🥇 الفائز: {winner}"
        );

        Games.Remove(chatId);
    }

    private async Task ShowHelp()
    {
        await Reply(
            "🎭 أوامر بوت مزاج 🎭\n\n" +

            "!مزاج جديد 400 4\n" +
            "إنشاء لعبة\n\n" +

            "!مزاج انضم احمر\n" +
            "!مزاج انضم ازرق\n" +
            "!مزاج انضم اصفر\n" +
            "!مزاج انضم بنفسجي\n\n" +

            "!مزاج تغيير احمر\n" +
            "تغيير الفريق\n\n" +

            "!مزاج لاعبين\n" +
            "عرض اللاعبين\n\n" +

            "!مزاج بدء\n" +
            "بدء اللعبة\n\n" +

            "!مزاج بطاقات\n" +
            "عرض أرقام البطاقات\n\n" +

            "🃏 أثناء اللعبة:\n" +
            "اكتب الرقم فقط:\n" +
            "13\n" +
            "أو\n" +
            "١٣\n\n" +

            "🎯 لا تحتاج كتابة:\n" +
            "!مزاج اختار"
        );
    }

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

    private static bool TryParseNumber(
        string text,
        out int number)
    {
        return int.TryParse(
            ToEnglishNumbers(
                text.Trim()),
            out number);
    }
}

}
