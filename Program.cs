using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;

public static class Program
{
    private static IWolfClient? _client;
    private static MazajGame? _game;

    private static readonly HashSet<string> _processedMessages = new();
    private static readonly object _messageLock = new();

    public static async Task Main()
    {
        Console.WriteLine("=================================");
        Console.WriteLine("        MAZAJ BOT");
        Console.WriteLine("=================================");

        string email = Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";
        string password = Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("❌ WOLF_EMAIL أو WOLF_PASSWORD غير موجود.");
            return;
        }

        try
        {
            Console.WriteLine("🔄 إنشاء اتصال Wolf...");

            _client = new WolfClient();

            Console.WriteLine("🔐 تسجيل الدخول...");

            await _client.Login(email, password);

            Console.WriteLine("✅ تم تسجيل الدخول.");

            /*
             * مهم:
             * لا نستخدم GetUser() داخل استقبال الرسائل،
             * لأن WolfLive.Api 1.2.3 قد يسبب Crash أثناء معالجة بعض الرسائل.
             */

            _client.Messaging.OnMessage += async (client, message) =>
            {
                try
                {
                    await ProcessMessageSafely(client, message);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"❌ خطأ في معالجة الرسالة: {ex.GetType().Name}: {ex.Message}"
                    );
                }
            };

            Console.WriteLine("🔌 الاتصال بالسيرفر...");

            await _client.Connect();

            Console.WriteLine("=================================");
            Console.WriteLine("🟢 البوت متصل ويعمل.");
            Console.WriteLine("=================================");

            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Console.WriteLine("=================================");
            Console.WriteLine("❌ حصل خطأ:");
            Console.WriteLine(ex.GetType().Name);
            Console.WriteLine(ex.Message);
            Console.WriteLine("=================================");

            if (ex.InnerException != null)
            {
                Console.WriteLine("Inner Exception:");
                Console.WriteLine(ex.InnerException.Message);
            }
        }
    }

    private static async Task ProcessMessageSafely(
        IWolfClient client,
        Message message)
    {
        try
        {
            if (message == null)
                return;

            /*
             * منع تكرار نفس الرسالة
             */
            if (!string.IsNullOrWhiteSpace(message.MessageId))
            {
                lock (_messageLock)
                {
                    if (_processedMessages.Contains(message.MessageId))
                        return;

                    _processedMessages.Add(message.MessageId);

                    if (_processedMessages.Count > 5000)
                    {
                        _processedMessages.Clear();
                    }
                }
            }

            string text = message.Content?.Trim() ?? "";

            if (string.IsNullOrWhiteSpace(text))
                return;

            Console.WriteLine(
                $"📩 رسالة: {text}"
            );

            /*
             * إذا كتب اللاعب رقم مباشر أثناء اللعبة
             */
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

            /*
             * أوامر البوت تبدأ بـ !مزاج
             */
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
                $"❌ COMMAND ERROR: {ex.GetType().Name}: {ex.Message}"
            );
        }
    }

    private static async Task HandleCommand(
        IWolfClient client,
        Message message,
        string command)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(command))
            {
                await Help(client, message);
                return;
            }

            string[] parts = command
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                );

            if (parts.Length == 0)
            {
                await Help(client, message);
                return;
            }

            string action = parts[0].Trim().ToLowerInvariant();

            switch (action)
            {
                case "جديد":
                    await NewGame(client, message);
                    break;

                case "انضم":
                    if (parts.Length < 2)
                    {
                        await client.Reply(
                            message,
                            "❌ الاستخدام:\n!مزاج انضم احمر\nأو\n!مزاج انضم ازرق"
                        );
                        return;
                    }

                    await JoinTeam(
                        client,
                        message,
                        parts[1]
                    );
                    break;

                case "تغيير":
                    if (parts.Length < 2)
                    {
                        await client.Reply(
                            message,
                            "❌ الاستخدام:\n!مزاج تغيير احمر\nأو\n!مزاج تغيير ازرق"
                        );
                        return;
                    }

                    await ChangeTeam(
                        client,
                        message,
                        parts[1]
                    );
                    break;

                case "لاعبين":
                    await ShowPlayers(client, message);
                    break;

                case "بدء":
                    await StartGame(client, message);
                    break;

                case "اختار":
                    if (parts.Length < 2 ||
                        !TryParseNumber(parts[1], out int number))
                    {
                        await client.Reply(
                            message,
                            "❌ الاستخدام:\n!مزاج اختار 25"
                        );
                        return;
                    }

                    await ChooseCard(
                        client,
                        message,
                        number,
                        false
                    );
                    break;

                case "بطاقات":
                    await ShowCards(client, message);
                    break;

                case "انهاء":
                    await EndGame(
                        client,
                        message,
                        "🛑 تم إنهاء اللعبة."
                    );
                    break;

                case "مساعدة":
                case "help":
                    await Help(client, message);
                    break;

                default:
                    await client.Reply(
                        message,
                        "❌ الأمر غير معروف.\nاكتب !مزاج مساعدة"
                    );
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ HandleCommand ERROR: {ex.Message}"
            );
        }
    }

    private static async Task NewGame(
        IWolfClient client,
        Message message)
    {
        try
        {
            if (_game != null && _game.Started)
            {
                await client.Reply(
                    message,
                    "⚠️ توجد لعبة قيد التشغيل حالياً."
                );
                return;
            }

            _game = new MazajGame();

            _game.GroupId =
                message.GroupId ?? "";

            await client.Reply(
                message,
                "🎭🔥 تم إنشاء لعبة المحبس!\n\n" +
                "🔴 الأمراء\n" +
                "🔵 النجوم\n\n" +
                "النقاط: 400 لكل فريق\n" +
                "عدد البطاقات: 65\n\n" +
                "للانضمام:\n" +
                "!مزاج انضم احمر\n" +
                "!مزاج انضم ازرق"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ NewGame ERROR: {ex.Message}"
            );
        }
    }

    private static async Task JoinTeam(
        IWolfClient client,
        Message message,
        string teamText)
    {
        if (_game == null)
        {
            await client.Reply(
                message,
                "❌ لا توجد لعبة.\nاكتب !مزاج جديد"
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

        string userId = message.UserId ?? "";

        if (string.IsNullOrWhiteSpace(userId))
        {
            await client.Reply(
                message,
                "❌ تعذر التعرف على حساب اللاعب."
            );
            return;
        }

        Team? team = _game.GetTeam(teamText);

        if (team == null)
        {
            await client.Reply(
                message,
                "❌ الفريق غير صحيح.\n\n" +
                "🔴 احمر = الأمراء\n" +
                "🔵 ازرق = النجوم"
            );
            return;
        }

        /*
         * لا نستخدم client.GetUser()
         * لتجنب Crash الموجود في WolfLive.Api.
         */
        string nickname =
            GetNickname(client, userId);

        foreach (Team otherTeam in _game.Teams)
        {
            if (otherTeam.Players.ContainsKey(userId))
            {
                if (otherTeam.Code == team.Code)
                {
                    await client.Reply(
                        message,
                        $"⚠️ {nickname} أنت موجود بالفعل في فريق {team.Name}."
                    );
                    return;
                }

                otherTeam.Players.Remove(userId);
            }
        }

        team.Players[userId] = nickname;

        await client.Reply(
            message,
            $"✅ انضم اللاعب {nickname} إلى فريق {team.Emoji} {team.Name}."
        );
    }

    private static async Task ChangeTeam(
        IWolfClient client,
        Message message,
        string teamText)
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

        string userId = message.UserId ?? "";

        if (string.IsNullOrWhiteSpace(userId))
        {
            await client.Reply(
                message,
                "❌ تعذر التعرف على حساب اللاعب."
            );
            return;
        }

        Team? newTeam =
            _game.GetTeam(teamText);

        if (newTeam == null)
        {
            await client.Reply(
                message,
                "❌ الفريق غير صحيح."
            );
            return;
        }

        string nickname =
            GetNickname(client, userId);

        foreach (Team team in _game.Teams)
        {
            team.Players.Remove(userId);
        }

        newTeam.Players[userId] = nickname;

        await client.Reply(
            message,
            $"🔄 تم نقل {nickname} إلى {newTeam.Emoji} {newTeam.Name}."
        );
    }

    /*
     * مهم جداً:
     * لا نستدعي client.GetUser هنا.
     */
    private static string GetNickname(
        IWolfClient client,
        string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return "لاعب";

        return userId;
    }

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
            "👥 لاعبو اللعبة:\n\n";

        foreach (Team team in _game.Teams)
        {
            result +=
                $"{team.Emoji} {team.Name}\n";

            if (team.Players.Count == 0)
            {
                result += "لا يوجد لاعبين\n\n";
                continue;
            }

            int index = 1;

            foreach (string player in team.Players.Values)
            {
                result +=
                    $"{index}. {player}\n";

                index++;
            }

            result += "\n";
        }

        await client.Reply(
            message,
            result
        );
    }

    private static async Task StartGame(
        IWolfClient client,
        Message message)
    {
        if (_game == null)
        {
            await client.Reply(
                message,
                "❌ لا توجد لعبة.\nاكتب !مزاج جديد"
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

        foreach (Team team in _game.Teams)
        {
            if (team.Players.Count == 0)
            {
                await client.Reply(
                    message,
                    $"❌ فريق {team.Name} لا يحتوي على لاعبين."
                );
                return;
            }
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
                "❌ لا يوجد لاعبين."
            );
            return;
        }

        _game.Started = true;
        _game.CurrentTurnIndex = 0;

        string board =
            BuildCardBoard(_game);

        string currentPlayer =
            _game.GetCurrentPlayerName();

        await client.Reply(
            message,
            "🎭🔥 بدأت لعبة المحبس!\n\n" +
            board +
            "\n\n" +
            $"🎯 الدور على: {currentPlayer}\n" +
            "⏱️ أمامك 25 ثانية."
        );

        _ = StartTurnTimer(
            client,
            message
        );
    }

    private static async Task ChooseCard(
        IWolfClient client,
        Message message,
        int number,
        bool directNumber)
    {
        if (_game == null)
        {
            await client.Reply(
                message,
                "❌ لا توجد لعبة."
            );
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

        if (number < 1 || number > 65)
        {
            await client.Reply(
                message,
                "❌ رقم البطاقة يجب أن يكون بين 1 و65."
            );
            return;
        }

        string userId =
            message.UserId ?? "";

        if (string.IsNullOrWhiteSpace(userId))
        {
            await client.Reply(
                message,
                "❌ تعذر التعرف على اللاعب."
            );
            return;
        }

        string currentUserId =
            _game.GetCurrentUserId();

        if (currentUserId != userId)
        {
            await client.Reply(
                message,
                $"⏳ ليس دورك.\nالدور حالياً على {_game.GetCurrentPlayerName()}."
            );
            return;
        }

        Card? card =
            _game.Cards.FirstOrDefault(
                x => x.Number == number
            );

        if (card == null)
        {
            await client.Reply(
                message,
                "❌ البطاقة غير موجودة."
            );
            return;
        }

        if (card.Used)
        {
            await client.Reply(
                message,
                "❌ هذه البطاقة مستخدمة مسبقاً."
            );
            return;
        }

        card.Used = true;

        Team currentTeam =
            _game.GetTeamByUser(userId)!;

        Team opponent =
            _game.GetOpponent(currentTeam)!;

        string result;

        if (card.Value > 0)
        {
            opponent.Score -= card.Value;

            result =
                $"🎴 البطاقة رقم {card.Number}\n" +
                $"💥 {card.Name}\n\n" +
                $"🔻 تم خصم {card.Value} نقطة من فريق {opponent.Name}";
        }
        else
        {
            int value =
                Math.Abs(card.Value);

            currentTeam.Score -= value;

            result =
                $"🎴 البطاقة رقم {card.Number}\n" +
                $"💥 {card.Name}\n\n" +
                $"🔻 تم خصم {value} نقطة من فريق {currentTeam.Name}";
        }

        string scores =
            $"\n\n🔴 الأمراء: {GetTeamScore("احمر")}\n" +
            $"🔵 النجوم: {GetTeamScore("ازرق")}";

        await client.Reply(
            message,
            result + scores
        );

        if (currentTeam.Score <= 0 ||
            opponent.Score <= 0)
        {
            string winner =
                GetWinnerText();

            await EndGame(
                client,
                message,
                winner
            );

            return;
        }

        if (_game.Cards.All(x => x.Used))
        {
            await EndGame(
                client,
                message,
                "🎴 انتهت جميع البطاقات."
            );

            return;
        }

        await Task.Delay(2000);

        if (_game == null ||
            !_game.Started)
        {
            return;
        }

        _game.NextTurn();

        await client.Reply(
            message,
            $"🔄 الدور انتقل إلى:\n" +
            $"🎯 {_game.GetCurrentPlayerName()}\n\n" +
            "⏱️ أمامه 25 ثانية."
        );

        _ = StartTurnTimer(
            client,
            message
        );
    }

    private static int GetTeamScore(
        string teamCode)
    {
        if (_game == null)
            return 0;

        Team? team =
            _game.GetTeam(teamCode);

        return team?.Score ?? 0;
    }

    private static async Task StartTurnTimer(
        IWolfClient client,
        Message message)
    {
        try
        {
            if (_game == null ||
                !_game.Started)
                return;

            string playerId =
                _game.GetCurrentUserId();

            int turnIndex =
                _game.CurrentTurnIndex;

            await Task.Delay(25000);

            if (_game == null ||
                !_game.Started)
                return;

            if (_game.CurrentTurnIndex != turnIndex)
                return;

            if (_game.GetCurrentUserId() != playerId)
                return;

            string playerName =
                _game.GetCurrentPlayerName();

            Team? team =
                _game.GetTeamByUser(playerId);

            if (team != null)
            {
                team.Players.Remove(playerId);
            }

            await client.Reply(
                message,
                $"⏰ انتهى الوقت على اللاعب {playerName}.\n" +
                "❌ تم إخراجه من اللعبة."
            );

            if (_game == null ||
                !_game.Started)
                return;

            foreach (Team checkTeam in _game.Teams)
            {
                if (checkTeam.Players.Count == 0)
                {
                    await EndGame(
                        client,
                        message,
                        $"🛑 انتهت اللعبة لأن فريق {checkTeam.Name} أصبح بدون لاعبين."
                    );

                    return;
                }
            }

            if (_game.TurnOrder.Count == 0)
            {
                await EndGame(
                    client,
                    message,
                    "🛑 انتهت اللعبة لعدم وجود لاعبين."
                );

                return;
            }

            _game.RemoveFromTurnOrder(
                playerId
            );

            if (_game.TurnOrder.Count == 0)
            {
                await EndGame(
                    client,
                    message,
                    "🛑 انتهت اللعبة."
                );

                return;
            }

            _game.CurrentTurnIndex =
                _game.CurrentTurnIndex %
                _game.TurnOrder.Count;

            await client.Reply(
                message,
                $"🎯 الدور الآن على:\n" +
                $"{_game.GetCurrentPlayerName()}\n\n" +
                "⏱️ لديك 25 ثانية."
            );

            _ = StartTurnTimer(
                client,
                message
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"❌ TIMER ERROR: {ex.Message}"
            );
        }
    }

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

        await client.Reply(
            message,
            BuildCardBoard(_game)
        );
    }

    private static string BuildCardBoard(
        MazajGame game)
    {
        string result =
            "🎴 بطاقات المحبس\n\n";

        foreach (Card card in game.Cards)
        {
            result += card.Used
                ? $"❌ {card.Number}   "
                : $"🟢 {card.Number}   ";

            if (card.Number % 5 == 0)
                result += "\n";
        }

        return result;
    }

    private static string GetWinnerText()
    {
        if (_game == null)
            return "🏆 انتهت اللعبة.";

        Team? winner =
            _game.Teams
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

        if (winner == null)
            return "🏆 انتهت اللعبة.";

        return
            $"🏆🔥 انتهت اللعبة!\n\n" +
            $"الفائز: {winner.Emoji} {winner.Name}\n" +
            $"النقاط: {winner.Score}";
    }

    private static async Task EndGame(
        IWolfClient client,
        Message message,
        string text)
    {
        if (_game != null)
        {
            _game.Started = false;
        }

        await client.Reply(
            message,
            text
        );
    }

    private static async Task Help(
        IWolfClient client,
        Message message)
    {
        string help =
            "🎭🔥 أوامر لعبة مزاج:\n\n" +

            "!مزاج جديد\n" +
            "إنشاء لعبة جديدة\n\n" +

            "!مزاج انضم احمر\n" +
            "الانضمام إلى فريق الأمراء 🔴\n\n" +

            "!مزاج انضم ازرق\n" +
            "الانضمام إلى فريق النجوم 🔵\n\n" +

            "!مزاج تغيير احمر\n" +
            "تغيير الفريق\n\n" +

            "!مزاج لاعبين\n" +
            "عرض اللاعبين\n\n" +

            "!مزاج بدء\n" +
            "بدء اللعبة\n\n" +

            "!مزاج اختار 25\n" +
            "اختيار البطاقة رقم 25\n\n" +

            "!مزاج بطاقات\n" +
            "عرض البطاقات\n\n" +

            "!مزاج انهاء\n" +
            "إنهاء اللعبة\n\n" +

            "💡 يمكنك أثناء اللعبة كتابة رقم البطاقة مباشرة.";

        await client.Reply(
            message,
            help
        );
    }

    private static string NormalizeTeam(
        string value)
    {
        value =
            value.Trim()
                .ToLowerInvariant();

        return value switch
        {
            "احمر" => "red",
            "الأحمر" => "red",
            "احمرَ" => "red",
            "red" => "red",

            "ازرق" => "blue",
            "الأزرق" => "blue",
            "ازرقَ" => "blue",
            "blue" => "blue",

            _ => value
        };
    }

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
}

public class MazajGame
{
    public string GroupId { get; set; } = "";

    public bool Started { get; set; }

    public int CurrentTurnIndex { get; set; }

    public List<string> TurnOrder { get; } =
        new();

    public List<Team> Teams { get; } =
        new();

    public List<Card> Cards { get; } =
        new();

    public MazajGame()
    {
        Teams.Add(
            new Team(
                "red",
                "🔴",
                "الأمراء"
            )
        );

        Teams.Add(
            new Team(
                "blue",
                "🔵",
                "النجوم"
            )
        );

        CreateCards();
    }

    private void CreateCards()
    {
        int number = 1;

        int[] positiveValues =
        {
            10, 10, 10, 10, 10,
            15, 15, 15, 15, 15,
            20, 20, 20, 20, 20,
            25, 25, 25, 25, 25,
            30, 30, 30, 30, 30,
            35, 35, 35, 35, 35,
            40, 40, 40, 40, 40,
            50, 50, 50, 50,
            60, 60, 60,
            75, 75,
            100
        };

        foreach (int value in positiveValues)
        {
            Cards.Add(
                new Card(
                    number,
                    $"خصم {value}",
                    value
                )
            );

            number++;
        }

        int[] negativeValues =
        {
            -10,
            -15,
            -20,
            -25,
            -30,
            -40,
            -50,
            -100
        };

        foreach (int value in negativeValues)
        {
            Cards.Add(
                new Card(
                    number,
                    $"مصيبة {Math.Abs(value)}",
                    value
                )
            );

            number++;
        }

        /*
         * التأكد من وجود 65 بطاقة.
         * إذا كانت القائمة أقل، نكمل بطاقات عادية.
         */
        while (Cards.Count < 65)
        {
            Cards.Add(
                new Card(
                    number,
                    "خصم 10",
                    10
                )
            );

            number++;
        }

        if (Cards.Count > 65)
        {
            Cards.RemoveRange(
                65,
                Cards.Count - 65
            );
        }
    }

    public Team? GetTeam(
        string value)
    {
        string normalized =
            ProgramNormalizeTeam(value);

        return Teams.FirstOrDefault(
            x => x.Code == normalized
        );
    }

    public Team? GetTeamByUser(
        string userId)
    {
        return Teams.FirstOrDefault(
            x => x.Players.ContainsKey(userId)
        );
    }

    public Team? GetOpponent(
        Team team)
    {
        return Teams.FirstOrDefault(
            x => x.Code != team.Code
        );
    }

    public string GetCurrentUserId()
    {
        if (TurnOrder.Count == 0)
            return "";

        if (CurrentTurnIndex < 0)
            CurrentTurnIndex = 0;

        if (CurrentTurnIndex >= TurnOrder.Count)
            CurrentTurnIndex = 0;

        return TurnOrder[
            CurrentTurnIndex
        ];
    }

    public string GetCurrentPlayerName()
    {
        string userId =
            GetCurrentUserId();

        Team? team =
            GetTeamByUser(userId);

        if (team == null)
            return userId;

        if (team.Players.TryGetValue(
                userId,
                out string? name))
        {
            return name;
        }

        return userId;
    }

    public void NextTurn()
    {
        if (TurnOrder.Count == 0)
            return;

        CurrentTurnIndex++;

        if (CurrentTurnIndex >= TurnOrder.Count)
            CurrentTurnIndex = 0;
    }

    public void RemoveFromTurnOrder(
        string userId)
    {
        TurnOrder.Remove(userId);

        if (TurnOrder.Count == 0)
        {
            CurrentTurnIndex = 0;
            return;
        }

        if (CurrentTurnIndex >= TurnOrder.Count)
        {
            CurrentTurnIndex = 0;
        }
    }

    private static string ProgramNormalizeTeam(
        string value)
    {
        value =
            value.Trim()
                .ToLowerInvariant();

        return value switch
        {
            "احمر" => "red",
            "الأحمر" => "red",
            "red" => "red",

            "ازرق" => "blue",
            "الأزرق" => "blue",
            "blue" => "blue",

            _ => value
        };
    }
}

public class Team
{
    public string Code { get; }

    public string Emoji { get; }

    public string Name { get; }

    public int Score { get; set; } = 400;

    public Dictionary<string, string> Players { get; } =
        new();

    public Team(
        string code,
        string emoji,
        string name)
    {
        Code = code;
        Emoji = emoji;
        Name = name;
    }
}

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
