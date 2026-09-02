using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WolfLive.Api;
using WolfLive.Api.Models;

class Program
{
    private static WolfClient _client = null!;
    private static BalloonGame? _game;

    static async Task Main()
    {
        string email = Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";
        string password = Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            Console.WriteLine("❌ WOLF_EMAIL أو WOLF_PASSWORD غير موجود.");
            return;
        }

        Console.WriteLine("🚀 BalloonBot يبدأ التشغيل...");
        Console.WriteLine("🔐 جاري تسجيل الدخول...");

        _client = new WolfClient();

        _client.OnConnected += (_) =>
        {
            Console.WriteLine("✅ تم الاتصال بـ WOLF.");
        };

        _client.Messaging.OnMessage += async (client, message) =>
        {
            try
            {
                string text = message.Content?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(text))
                    return;

                Console.WriteLine(
                    $"📩 Message: {text} | User: {message.UserId} | Group: {message.GroupId}"
                );

                await HandleMessage(client, message, text);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "❌ COMMAND ERROR: " + ex.Message
                );
            }
        };

        bool login = _client.Login(email, password);

        if (!login)
        {
            Console.WriteLine("❌ فشل تسجيل الدخول.");
            return;
        }

        Console.WriteLine("✅ تم تسجيل الدخول بنجاح.");

        await _client.Connect();

        Console.WriteLine("🚀 BalloonBot يعمل الآن.");

        await Task.Delay(Timeout.Infinite);
    }

    private static async Task HandleMessage(
        IWolfClient client,
        Message message,
        string text)
    {
        string userId = message.UserId;
        string groupId = message.GroupId ?? "";

        // =========================
        // أوامر البوت
        // =========================

        if (text.Equals("!بالونات", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("!بالونات مساعدة", StringComparison.OrdinalIgnoreCase))
        {
            await Reply(client, message, HelpText());
            return;
        }

        if (text.Equals("!بالونات جديد", StringComparison.OrdinalIgnoreCase))
        {
            if (_game != null)
            {
                await Reply(
                    client,
                    message,
                    "⚠️ توجد لعبة بالونات قيد الإنشاء أو اللعب حالياً."
                );

                return;
            }

            _game = new BalloonGame(groupId, userId);

            string nickname = await GetNickname(client, userId);

            _game.AddPlayer(userId, nickname);

            await Reply(
                client,
                message,
                "🎈 تم إنشاء لعبة البالونات!\n\n" +
                "👤 اللاعب الذي أنشأ اللعبة:\n" +
                $"{nickname} — 7 🎈\n\n" +
                "👥 لإضافة اللاعبين:\n" +
                "!بالونات انضم\n\n" +
                "🎮 بعد اكتمال اللاعبين استخدم:\n" +
                "!بالونات بدء"
            );

            return;
        }

        if (text.Equals("!بالونات انضم", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("!بالونات انضمام", StringComparison.OrdinalIgnoreCase))
        {
            if (_game == null)
            {
                await Reply(
                    client,
                    message,
                    "❌ لا توجد لعبة حالياً.\nاستخدم !بالونات جديد لإنشاء لعبة."
                );

                return;
            }

            if (_game.GroupId != groupId)
                return;

            if (_game.Started)
            {
                await Reply(
                    client,
                    message,
                    "❌ اللعبة بدأت بالفعل، لا يمكن الانضمام الآن."
                );

                return;
            }

            if (_game.HasPlayer(userId))
            {
                await Reply(
                    client,
                    message,
                    "⚠️ أنت مشترك بالفعل في اللعبة."
                );

                return;
            }

            string nickname = await GetNickname(client, userId);

            _game.AddPlayer(userId, nickname);

            await Reply(
                client,
                message,
                $"🎈 تم انضمام {nickname} إلى اللعبة!\n\n" +
                _game.PlayersText()
            );

            return;
        }

        if (text.Equals("!بالونات لاعبين", StringComparison.OrdinalIgnoreCase))
        {
            if (_game == null)
            {
                await Reply(
                    client,
                    message,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            if (_game.GroupId != groupId)
                return;

            await Reply(
                client,
                message,
                _game.PlayersText()
            );

            return;
        }

        if (text.Equals("!بالونات بدء", StringComparison.OrdinalIgnoreCase))
        {
            if (_game == null)
            {
                await Reply(
                    client,
                    message,
                    "❌ لا توجد لعبة حالياً.\nاستخدم !بالونات جديد"
                );

                return;
            }

            if (_game.GroupId != groupId)
                return;

            if (_game.Started)
            {
                await Reply(
                    client,
                    message,
                    "⚠️ اللعبة بدأت بالفعل."
                );

                return;
            }

            if (_game.Players.Count < 2)
            {
                await Reply(
                    client,
                    message,
                    "❌ يجب أن يكون هناك لاعبان على الأقل لبدء اللعبة."
                );

                return;
            }

            _game.StartGame();

            await Reply(
                client,
                message,
                "🎈🔥 بدأت لعبة البالونات!\n\n" +
                _game.PlayersText() +
                "\n\n" +
                $"🎯 الدور الآن على: {_game.CurrentPlayerName}\n\n" +
                "اختر رقم اللاعب الذي تريد تفجير بالوناته."
            );

            return;
        }

        if (text.Equals("!بالونات انهاء", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("!بالونات إنهاء", StringComparison.OrdinalIgnoreCase))
        {
            if (_game == null)
            {
                await Reply(
                    client,
                    message,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            if (_game.GroupId != groupId)
                return;

            _game = null;

            await Reply(
                client,
                message,
                "🛑 تم إنهاء لعبة البالونات."
            );

            return;
        }

        // =========================
        // اللعب بالأرقام
        // =========================

        if (_game == null)
            return;

        if (_game.GroupId != groupId)
            return;

        if (!_game.Started)
            return;

        if (!_game.IsCurrentPlayer(userId))
            return;

        if (!int.TryParse(text, out int number))
            return;

        // اختيار الخصم
        if (_game.WaitingForOpponent)
        {
            BalloonPlayer? opponent =
                _game.GetPlayerByNumber(number);

            if (opponent == null)
            {
                await Reply(
                    client,
                    message,
                    "❌ رقم اللاعب غير صحيح.\n" +
                    _game.PlayersText()
                );

                return;
            }

            if (opponent.UserId == userId)
            {
                await Reply(
                    client,
                    message,
                    "❌ لا يمكنك اختيار نفسك."
                );

                return;
            }

            _game.SelectedOpponentId = opponent.UserId;
            _game.WaitingForOpponent = false;
            _game.WaitingForBalloon = true;

            await Reply(
                client,
                message,
                $"🎯 اخترت اللاعب: {opponent.Name}\n\n" +
                $"🎈 بالونات {opponent.Name}:\n" +
                string.Join(
                    " ",
                    opponent.ActiveBalloons.Select(x => $"{x}🎈")
                ) +
                "\n\n" +
                "أرسل رقم البالون الذي تريد اختياره."
            );

            return;
        }

        // اختيار البالون
        if (_game.WaitingForBalloon)
        {
            await PlayBalloon(client, message, number);
        }
    }

    // =========================
    // تنفيذ ضربة البالون
    // =========================

    private static async Task PlayBalloon(
        IWolfClient client,
        Message message,
        int balloonNumber)
    {
        if (_game == null)
            return;

        BalloonPlayer? attacker =
            _game.GetCurrentPlayer();

        if (attacker == null)
            return;

        BalloonPlayer? opponent =
            _game.GetPlayerById(_game.SelectedOpponentId ?? "");

        if (opponent == null)
        {
            _game.ResetSelection();

            await Reply(
                client,
                message,
                "❌ الخصم غير موجود."
            );

            return;
        }

        if (!opponent.ActiveBalloons.Contains(balloonNumber))
        {
            await Reply(
                client,
                message,
                "❌ رقم البالون غير صحيح.\n\n" +
                "البالونات المتوفرة:\n" +
                string.Join(
                    " ",
                    opponent.ActiveBalloons.Select(x => $"{x}🎈")
                )
            );

            return;
        }

        string result;

        int random = Random.Shared.Next(1, 101);

        // 🍀 حظ
        if (random <= 15)
        {
            result =
                $"🍀 حظ!\n\n" +
                $"🎈 البالون رقم {balloonNumber} لم ينفجر!\n" +
                $"😎 {opponent.Name} نجا من الضربة!";

            _game.ResetSelection();
            _game.MoveToNextPlayer();

            result +=
                $"\n\n➡️ الدور الآن على: {_game.CurrentPlayerName}";
        }

        // 🛡️ نجاة
        else if (random <= 30)
        {
            result =
                $"🛡️ نجاة!\n\n" +
                $"🎈 البالون رقم {balloonNumber} بقي موجوداً.\n" +
                $"➡️ لكن الدور ينتقل إلى اللاعب التالي.";

            _game.ResetSelection();
            _game.MoveToNextPlayer();

            result +=
                $"\n\n➡️ الدور الآن على: {_game.CurrentPlayerName}";
        }

        // 🔄 دور إضافي
        else if (random <= 40)
        {
            opponent.PopBalloon(balloonNumber);

            result =
                $"🔄 دور إضافي!\n\n" +
                $"💥 انفجر البالون رقم {balloonNumber}!\n" +
                $"🎈 {opponent.Name} أصبح لديه {opponent.BalloonsCount} بالون.";

            _game.ResetSelection();

            if (opponent.BalloonsCount <= 0)
            {
                opponent.Eliminated = true;
                opponent.ActiveBalloons.Clear();

                result +=
                    $"\n\n💀 تم إقصاء {opponent.Name} من اللعبة!";

                if (_game.AlivePlayers.Count == 1)
                {
                    BalloonPlayer winner =
                        _game.GetWinner()!;

                    result +=
                        $"\n\n🏆🎉 الفائز هو: {winner.Name} 🎉🏆" +
                        "\n\n🎈 انتهت اللعبة!";

                    _game = null;
                }
                else
                {
                    result +=
                        $"\n\n🔄 لديك دور إضافي يا {attacker.Name}!" +
                        "\nاختر لاعباً آخر.";
                }
            }
            else
            {
                result +=
                    $"\n\n🔄 لديك دور إضافي يا {attacker.Name}!" +
                    "\nاختر لاعباً آخر.";
            }
        }

        // 💥 ضربة عادية
        else
        {
            opponent.PopBalloon(balloonNumber);

            result =
                $"💥 بووووم!\n\n" +
                $"🎈 انفجر البالون رقم {balloonNumber}!\n" +
                $"😈 {opponent.Name} خسر بالوناً.\n" +
                $"🎈 المتبقي لديه: {opponent.BalloonsCount}";

            _game.ResetSelection();

            if (opponent.BalloonsCount <= 0)
            {
                opponent.Eliminated = true;
                opponent.ActiveBalloons.Clear();

                result +=
                    $"\n\n💀 تم إقصاء {opponent.Name} من اللعبة!";

                if (_game.AlivePlayers.Count == 1)
                {
                    BalloonPlayer winner =
                        _game.GetWinner()!;

                    result +=
                        $"\n\n🏆🎉 الفائز هو: {winner.Name} 🎉🏆" +
                        "\n\n🎈 انتهت اللعبة!";

                    _game = null;
                }
                else
                {
                    _game.MoveToNextPlayer();

                    result +=
                        $"\n\n➡️ الدور الآن على: {_game.CurrentPlayerName}";
                }
            }
            else
            {
                _game.MoveToNextPlayer();

                result +=
                    $"\n\n➡️ الدور الآن على: {_game.CurrentPlayerName}";
            }
        }

        await Reply(
            client,
            message,
            result
        );
    }

    // =========================
    // بدء اللعبة
    // =========================

    private static string HelpText()
    {
        return
            "🎈🔥 لعبة البالونات 🔥🎈\n\n" +

            "🎮 الأوامر:\n\n" +

            "🎈 !بالونات جديد\n" +
            "إنشاء لعبة جديدة.\n\n" +

            "👥 !بالونات انضم\n" +
            "الانضمام إلى اللعبة.\n\n" +

            "📋 !بالونات لاعبين\n" +
            "عرض اللاعبين والبالونات.\n\n" +

            "▶️ !بالونات بدء\n" +
            "بدء اللعبة.\n\n" +

            "🛑 !بالونات انهاء\n" +
            "إنهاء اللعبة.\n\n" +

            "🎯 طريقة اللعب:\n" +
            "كل لاعب يبدأ بـ 7 بالونات.\n\n" +

            "1️⃣ في دورك أرسل رقم اللاعب.\n" +
            "مثال: 3\n\n" +

            "2️⃣ بعدها البوت يعرض بالونات الخصم.\n\n" +

            "3️⃣ أرسل رقم البالون.\n" +
            "مثال: 5\n\n" +

            "💥 إذا انفجر البالون ينقص واحد من بالونات الخصم.\n\n" +

            "💀 اللاعب الذي يصل إلى 0 بالونات يتم إقصاؤه.\n\n" +

            "🏆 آخر لاعب يبقى هو الفائز.\n\n" +

            "🍀 توجد تأثيرات عشوائية أثناء اللعب.";
    }

    // =========================
    // الحصول على اسم اللاعب
    // =========================

    private static async Task<string> GetNickname(
        IWolfClient client,
        string userId)
    {
        try
        {
            var user = await client.GetUser(userId);

            if (user != null &&
                !string.IsNullOrWhiteSpace(user.Nickname))
            {
                return user.Nickname;
            }
        }
        catch
        {
        }

        return userId;
    }

    // =========================
    // إرسال الرد
    // =========================

    private static async Task Reply(
        IWolfClient client,
        Message message,
        string text)
    {
        try
        {
            await client.Reply(message, text);
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "❌ Reply Error: " + ex.Message
            );
        }
    }
}

// ======================================================
// اللاعب
// ======================================================

class BalloonPlayer
{
    public string UserId { get; set; }

    public string Name { get; set; }

    public bool Eliminated { get; set; }

    public List<int> ActiveBalloons { get; set; }

    public int BalloonsCount =>
        ActiveBalloons.Count;

    public BalloonPlayer(
        string userId,
        string name)
    {
        UserId = userId;
        Name = name;

        Eliminated = false;

        ActiveBalloons =
            Enumerable.Range(1, 7)
            .ToList();
    }

    public void PopBalloon(int number)
    {
        ActiveBalloons.Remove(number);
    }
}

// ======================================================
// اللعبة
// ======================================================

class BalloonGame
{
    public string GroupId { get; }

    public string CreatorId { get; }

    public bool Started { get; private set; }

    public List<BalloonPlayer> Players { get; }

    public string? CurrentPlayerId { get; private set; }

    public string? SelectedOpponentId { get; set; }

    public bool WaitingForOpponent { get; set; }

    public bool WaitingForBalloon { get; set; }

    public BalloonGame(
        string groupId,
        string creatorId)
    {
        GroupId = groupId;
        CreatorId = creatorId;

        Started = false;

        Players =
            new List<BalloonPlayer>();

        WaitingForOpponent = false;
        WaitingForBalloon = false;
    }

    // =========================
    // إضافة لاعب
    // =========================

    public void AddPlayer(
        string userId,
        string name)
    {
        if (HasPlayer(userId))
            return;

        Players.Add(
            new BalloonPlayer(
                userId,
                name
            )
        );
    }

    // =========================
    // هل اللاعب موجود؟
    // =========================

    public bool HasPlayer(string userId)
    {
        return Players.Any(
            x => x.UserId == userId
        );
    }

    // =========================
    // اللاعبين الأحياء
    // =========================

    public List<BalloonPlayer> AlivePlayers =>
        Players
            .Where(x => !x.Eliminated)
            .ToList();

    // =========================
    // بدء اللعبة
    // =========================

    public void StartGame()
    {
        Started = true;

        CurrentPlayerId =
            AlivePlayers.First().UserId;

        WaitingForOpponent = true;
        WaitingForBalloon = false;
        SelectedOpponentId = null;
    }

    // =========================
    // اللاعب الحالي
    // =========================

    public BalloonPlayer? GetCurrentPlayer()
    {
        if (string.IsNullOrWhiteSpace(CurrentPlayerId))
            return null;

        return GetPlayerById(
            CurrentPlayerId
        );
    }

    public string CurrentPlayerName
    {
        get
        {
            return GetCurrentPlayer()?.Name
                   ?? "غير معروف";
        }
    }

    // =========================
    // التحقق من الدور
    // =========================

    public bool IsCurrentPlayer(
        string userId)
    {
        return CurrentPlayerId == userId;
    }

    // =========================
    // الحصول على لاعب بواسطة ID
    // =========================

    public BalloonPlayer? GetPlayerById(
        string userId)
    {
        return Players.FirstOrDefault(
            x => x.UserId == userId
        );
    }

    // =========================
    // الحصول على لاعب بواسطة الرقم
    // =========================

    public BalloonPlayer? GetPlayerByNumber(
        int number)
    {
        var alive =
            AlivePlayers;

        if (number < 1 ||
            number > alive.Count)
        {
            return null;
        }

        return alive[number - 1];
    }

    // =========================
    // الانتقال للاعب التالي
    // =========================

    public void MoveToNextPlayer()
    {
        var alive =
            AlivePlayers;

        if (alive.Count == 0)
        {
            CurrentPlayerId = null;
            return;
        }

        int currentIndex =
            alive.FindIndex(
                x => x.UserId == CurrentPlayerId
            );

        if (currentIndex < 0)
        {
            CurrentPlayerId =
                alive[0].UserId;
        }
        else
        {
            int nextIndex =
                (currentIndex + 1) %
                alive.Count;

            CurrentPlayerId =
                alive[nextIndex].UserId;
        }

        WaitingForOpponent = true;
        WaitingForBalloon = false;
        SelectedOpponentId = null;
    }

    // =========================
    // إعادة حالة الاختيار
    // =========================

    public void ResetSelection()
    {
        WaitingForOpponent = true;
        WaitingForBalloon = false;
        SelectedOpponentId = null;
    }

    // =========================
    // الفائز
    // =========================

    public BalloonPlayer? GetWinner()
    {
        return AlivePlayers.Count == 1
            ? AlivePlayers[0]
            : null;
    }

    // =========================
    // عرض اللاعبين
    // =========================

    public string PlayersText()
    {
        if (Players.Count == 0)
            return "👥 لا يوجد لاعبين.";

        var lines =
            new List<string>();

        int number = 1;

        foreach (var player in Players)
        {
            string status =
                player.Eliminated
                    ? " 💀 مقصى"
                    : "";

            lines.Add(
                $"{GetNumberEmoji(number)} " +
                $"{player.Name} — " +
                $"{player.BalloonsCount} 🎈" +
                status
            );

            if (!player.Eliminated)
                number++;
        }

        return
            "👥 اللاعبين:\n\n" +
            string.Join(
                "\n",
                lines
            );
    }

    private string GetNumberEmoji(
        int number)
    {
        return number switch
        {
            1 => "1️⃣",
            2 => "2️⃣",
            3 => "3️⃣",
            4 => "4️⃣",
            5 => "5️⃣",
            6 => "6️⃣",
            7 => "7️⃣",
            8 => "8️⃣",
            9 => "9️⃣",
            10 => "🔟",
            _ => $"{number}."
        };
    }
}
