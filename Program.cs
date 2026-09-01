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
            Console.WriteLine("🎭🔥 Mazaj Bot Starting...");

            string email = Environment.GetEnvironmentVariable("WOLF_EMAIL") ?? "";
            string password = Environment.GetEnvironmentVariable("WOLF_PASSWORD") ?? "";

            if (string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                Console.WriteLine("❌ WOLF_EMAIL أو WOLF_PASSWORD غير موجودة.");
                return;
            }

            try
            {
                _client = new WolfClient();

                _client.Messaging.OnMessage += async (client, message) =>
                {
                    try
                    {
                        if (message == null)
                            return;

                        Console.WriteLine(
                            $"📩 User: {message.UserId} | Group: {message.GroupId} | Content: {message.Content}"
                        );

                        string content = (message.Content ?? "").Trim();

                        if (string.IsNullOrWhiteSpace(content))
                            return;

                        // اختيار البطاقة برقم فقط
                        if (TryParseNumber(content, out int cardNumber))
                        {
                            await ChooseCard(client, message, cardNumber);
                            return;
                        }

                        // أوامر !مزاج
                        if (content.StartsWith("!مزاج", StringComparison.OrdinalIgnoreCase))
                        {
                            string commandText = content.Substring(5).Trim();
                            await HandleCommand(client, message, commandText);
                            return;
                        }

                        // دعم !mazaj
                        if (content.StartsWith("!mazaj", StringComparison.OrdinalIgnoreCase))
                        {
                            string commandText = content.Substring(6).Trim();
                            await HandleCommand(client, message, commandText);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Message Error: {ex}");
                    }
                };

                await _client.Connect();

                Console.WriteLine("✅ Connected to Wolf.");

                await _client.Messaging.Initialize();

                Console.WriteLine("✅ Messaging initialized.");

                await Task.Delay(Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Bot Error: {ex}");
            }
        }

        private static async Task HandleCommand(
            IWolfClient client,
            Message message,
            string commandText)
        {
            string[] parts = commandText.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries
            );

            if (parts.Length == 0)
            {
                await SendHelp(client, message);
                return;
            }

            string command = parts[0].ToLowerInvariant();

            switch (command)
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
                        TryParseNumber(parts[1], out int selectedNumber))
                    {
                        await ChooseCard(client, message, selectedNumber);
                    }
                    else
                    {
                        await message.Reply(
                            client,
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
                    await message.Reply(
                        client,
                        "❌ الأمر غير معروف.\nاكتب !مزاج مساعدة"
                    );
                    break;
            }
        }

        private static async Task NewGame(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (parts.Length < 3)
            {
                await message.Reply(
                    client,
                    "❌ الاستخدام الصحيح:\n" +
                    "!مزاج جديد <النقاط لكل بطاقة> <عدد الفرق>\n\n" +
                    "مثال:\n!مزاج جديد 2 4"
                );

                return;
            }

            if (!int.TryParse(parts[1], out int points))
            {
                await message.Reply(
                    client,
                    "❌ عدد النقاط غير صحيح."
                );

                return;
            }

            if (!int.TryParse(parts[2], out int teamCount))
            {
                await message.Reply(
                    client,
                    "❌ عدد الفرق غير صحيح."
                );

                return;
            }

            if (points <= 0)
            {
                await message.Reply(
                    client,
                    "❌ النقاط يجب أن تكون أكبر من صفر."
                );

                return;
            }

            if (teamCount < 2 || teamCount > 4)
            {
                await message.Reply(
                    client,
                    "❌ عدد الفرق يجب أن يكون من 2 إلى 4."
                );

                return;
            }

            _game = new MazajGame(points, teamCount);

            _game.GroupId = message.GroupId ?? "";

            if (!string.IsNullOrWhiteSpace(_game.GroupId))
            {
                try
                {
                    await client.Messaging.GroupMessageSubscribe(
                        _game.GroupId
                    );

                    Console.WriteLine(
                        $"✅ Subscribed to group: {_game.GroupId}"
                    );
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"⚠️ Group subscribe error: {ex.Message}"
                    );
                }
            }

            await message.Reply(
                client,
                $"🎭🔥 تم إنشاء لعبة مزاج بنجاح!\n\n" +
                $"💰 النقاط لكل بطاقة: {points}\n" +
                $"👥 عدد الفرق: {teamCount}\n" +
                $"🎴 عدد البطاقات: 65\n\n" +
                $"🟢 الخطوة التالية:\n" +
                $"!مزاج انضم <الفريق>\n\n" +
                $"🔴 احمر\n" +
                $"🔵 ازرق\n" +
                $"🟡 اصفر\n" +
                $"🟣 بنفسجي\n\n" +
                $"بعد اكتمال اللاعبين:\n" +
                $"!مزاج بدء"
            );
        }

        private static async Task JoinTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await message.Reply(
                    client,
                    "❌ لا توجد لعبة حالياً.\nاكتب !مزاج جديد"
                );

                return;
            }

            if (_game.Started)
            {
                await message.Reply(
                    client,
                    "❌ اللعبة بدأت بالفعل."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await message.Reply(
                    client,
                    "❌ الاستخدام:\n!مزاج انضم <احمر|ازرق|اصفر|بنفسجي>"
                );

                return;
            }

            string teamName = NormalizeTeam(parts[1]);

            if (string.IsNullOrWhiteSpace(teamName))
            {
                await message.Reply(
                    client,
                    "❌ الفريق غير صحيح.\n\n" +
                    "🔴 احمر\n" +
                    "🔵 ازرق\n" +
                    "🟡 اصفر\n" +
                    "🟣 بنفسجي"
                );

                return;
            }

            Team? team = _game.Teams.FirstOrDefault(
                t => t.Name == teamName
            );

            if (team == null)
            {
                await message.Reply(
                    client,
                    "❌ هذا الفريق غير موجود."
                );

                return;
            }

            if (_game.GetTeamByPlayer(message.UserId) != null)
            {
                await message.Reply(
                    client,
                    "❌ أنت منضم إلى فريق بالفعل."
                );

                return;
            }

            if (team.Players.ContainsKey(message.UserId))
            {
                await message.Reply(
                    client,
                    "❌ أنت موجود في هذا الفريق بالفعل."
                );

                return;
            }

            string nickname = await GetNickname(
                client,
                message.UserId
            );

            team.Players[message.UserId] = nickname;

            await message.Reply(
                client,
                $"✅ تم انضمامك إلى الفريق {team.Emoji} {team.Name}\n" +
                $"👤 اللاعب: {nickname}"
            );
        }

        private static async Task ChangeTeam(
            IWolfClient client,
            Message message,
            string[] parts)
        {
            if (_game == null)
            {
                await message.Reply(
                    client,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            if (_game.Started)
            {
                await message.Reply(
                    client,
                    "❌ لا يمكن تغيير الفريق بعد بدء اللعبة."
                );

                return;
            }

            if (parts.Length < 2)
            {
                await message.Reply(
                    client,
                    "❌ الاستخدام:\n!مزاج تغيير <الفريق>"
                );

                return;
            }

            string newTeamName = NormalizeTeam(parts[1]);

            if (string.IsNullOrWhiteSpace(newTeamName))
            {
                await message.Reply(
                    client,
                    "❌ الفريق غير صحيح."
                );

                return;
            }

            Team? oldTeam = _game.GetTeamByPlayer(
                message.UserId
            );

            if (oldTeam == null)
            {
                await message.Reply(
                    client,
                    "❌ أنت غير منضم لأي فريق."
                );

                return;
            }

            Team? newTeam = _game.Teams.FirstOrDefault(
                t => t.Name == newTeamName
            );

            if (newTeam == null)
            {
                await message.Reply(
                    client,
                    "❌ الفريق غير موجود."
                );

                return;
            }

            if (oldTeam.Name == newTeam.Name)
            {
                await message.Reply(
                    client,
                    "❌ أنت موجود بهذا الفريق أصلاً."
                );

                return;
            }

            string nickname = oldTeam.Players[message.UserId];

            oldTeam.Players.Remove(message.UserId);

            newTeam.Players[message.UserId] = nickname;

            await message.Reply(
                client,
                $"✅ تم تغيير فريقك إلى {newTeam.Emoji} {newTeam.Name}"
            );
        }

        private static async Task ShowPlayers(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await message.Reply(
                    client,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            string text = "👥 قائمة اللاعبين\n\n";

            foreach (Team team in _game.Teams)
            {
                text += $"{team.Emoji} {team.Name}\n";

                if (team.Players.Count == 0)
                {
                    text += "└ لا يوجد لاعبين\n\n";
                    continue;
                }

                foreach (string player in team.Players.Values)
                {
                    text += $"└ 👤 {player}\n";
                }

                text += "\n";
            }

            await message.Reply(client, text);
        }

        private static async Task StartGame(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await message.Reply(
                    client,
                    "❌ لا توجد لعبة.\nاكتب !مزاج جديد أولاً."
                );

                return;
            }

            if (_game.Started)
            {
                await message.Reply(
                    client,
                    "❌ اللعبة بدأت بالفعل."
                );

                return;
            }

            _game.TurnOrder.Clear();

            foreach (Team team in _game.Teams)
            {
                foreach (string playerId in team.Players.Keys)
                {
                    _game.TurnOrder.Add(playerId);
                }
            }

            if (_game.TurnOrder.Count == 0)
            {
                await message.Reply(
                    client,
                    "❌ يجب أن ينضم لاعب واحد على الأقل."
                );

                return;
            }

            _game.Started = true;
            _game.CurrentPlayerIndex = 0;
            _game.TurnVersion++;

            string board =
                "🎭🔥 لعبة مزاج بدأت ⚡\n\n" +
                "📊 لوحة النتائج\n" +
                BuildScoreBoard() +
                "\n\n" +
                BuildCardBoard() +
                "\n\n" +
                $"👤 اللاعب التالي: {_game.CurrentPlayerName}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await message.Reply(client, board);

            _ = StartTurnTimer(
                client,
                _game.TurnVersion
            );
        }

        private static async Task ChooseCard(
            IWolfClient client,
            Message message,
            int cardNumber)
        {
            if (_game == null || !_game.Started)
                return;

            if (cardNumber < 1 || cardNumber > 65)
                return;

            // فقط صاحب الدور
            if (message.UserId != _game.CurrentPlayerId)
                return;

            Card? card = _game.Cards.FirstOrDefault(
                c => c.Number == cardNumber
            );

            if (card == null)
                return;

            if (card.Used)
            {
                await message.Reply(
                    client,
                    "❌ هذه البطاقة مستخدمة مسبقاً."
                );

                return;
            }

            card.Used = true;

            _game.TurnVersion++;

            Team? playerTeam = _game.GetTeamByPlayer(
                message.UserId
            );

            if (playerTeam == null)
                return;

            playerTeam.Score += card.Value;

            string resultText;

            if (card.Value >= 0)
            {
                resultText =
                    $"{playerTeam.Emoji} الفريق {playerTeam.Name} ربح {card.Value} نقطة";
            }
            else
            {
                resultText =
                    $"{playerTeam.Emoji} الفريق {playerTeam.Name} خسر {Math.Abs(card.Value)} نقطة";
            }

            MoveToNextPlayer();

            string response =
                "🎴 تم اختيار البطاقة رقم " +
                card.Number +
                "\n\n" +
                $"🃏 البطاقة: {card.Name}\n" +
                $"💰 القيمة: {FormatValue(card.Value)}\n\n" +
                $"{resultText}\n\n" +
                "📊 لوحة النتائج\n" +
                BuildScoreBoard() +
                "\n\n" +
                BuildCardBoard();

            if (_game.AllCardsUsed)
            {
                response +=
                    "\n\n🏁 انتهت جميع البطاقات!\n\n" +
                    BuildFinalResults();

                _game.Started = false;

                await message.Reply(
                    client,
                    response
                );

                return;
            }

            response +=
                "\n\n" +
                $"👤 اللاعب التالي: {_game.CurrentPlayerName}\n" +
                "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

            await message.Reply(
                client,
                response
            );

            _ = StartTurnTimer(
                client,
                _game.TurnVersion
            );
        }

        private static async Task StartTurnTimer(
            IWolfClient client,
            int version)
        {
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(25)
                );

                if (_game == null)
                    return;

                if (!_game.Started)
                    return;

                if (_game.TurnVersion != version)
                    return;

                string skippedPlayer =
                    _game.CurrentPlayerName;

                _game.TurnVersion++;

                MoveToNextPlayer();

                if (_game.AllCardsUsed)
                {
                    _game.Started = false;

                    await client.GroupMessage(
                        _game.GroupId,
                        $"⏰ انتهى الوقت!\n" +
                        $"👤 اللاعب {skippedPlayer} لم يختر بطاقة.\n\n" +
                        "🏁 انتهت اللعبة."
                    );

                    return;
                }

                string text =
                    $"⏰ انتهى الوقت!\n" +
                    $"👤 اللاعب {skippedPlayer} لم يختر بطاقة.\n\n" +
                    "🎴 لوحة الأرقام\n" +
                    BuildCardBoard() +
                    "\n\n" +
                    "📊 لوحة النتائج\n" +
                    BuildScoreBoard() +
                    "\n\n" +
                    $"👤 اللاعب التالي: {_game.CurrentPlayerName}\n" +
                    "⏱️ عندك 25 ثانية تختار واحد من الأرقام";

                await client.GroupMessage(
                    _game.GroupId,
                    text
                );

                _ = StartTurnTimer(
                    client,
                    _game.TurnVersion
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"❌ Timer Error: {ex.Message}"
                );
            }
        }

        private static void MoveToNextPlayer()
        {
            if (_game == null)
                return;

            if (_game.TurnOrder.Count == 0)
                return;

            _game.CurrentPlayerIndex++;

            if (_game.CurrentPlayerIndex >=
                _game.TurnOrder.Count)
            {
                _game.CurrentPlayerIndex = 0;
            }
        }

        private static async Task EndGame(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await message.Reply(
                    client,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            _game.Started = false;
            _game.TurnVersion++;

            string results = BuildFinalResults();

            await message.Reply(
                client,
                "🛑 تم إنهاء لعبة مزاج.\n\n" +
                results
            );

            _game = null;
        }

        private static string BuildScoreBoard()
        {
            if (_game == null)
                return "";

            string text = "";

            foreach (Team team in _game.Teams)
            {
                text +=
                    $"{team.Emoji} {team.Name}: {team.Score} نقطة\n";
            }

            return text.TrimEnd();
        }

        private static string BuildCardBoard()
        {
            if (_game == null)
                return "";

            string text =
                "🎴 لوحة الأرقام\n\n";

            for (int i = 1; i <= 65; i++)
            {
                Card? card = _game.Cards.FirstOrDefault(
                    c => c.Number == i
                );

                string number =
                    card != null && card.Used
                        ? "❌"
                        : i.ToString("00");

                text += number.PadLeft(3);

                if (i < 65)
                {
                    text += " | ";

                    if (i % 8 == 0)
                        text += "\n";
                }
            }

            return text.TrimEnd();
        }

        private static async Task ShowCards(
            IWolfClient client,
            Message message)
        {
            if (_game == null)
            {
                await message.Reply(
                    client,
                    "❌ لا توجد لعبة حالياً."
                );

                return;
            }

            string text = "🎴 البطاقات\n\n";

            foreach (Card card in _game.Cards)
            {
                string status =
                    card.Used
                        ? "❌ مستخدمة"
                        : "🟢 متاحة";

                text +=
                    $"{card.Number}. {card.Name} | " +
                    $"{FormatValue(card.Value)} | {status}\n";
            }

            await message.Reply(
                client,
                text
            );
        }

        private static async Task SendHelp(
            IWolfClient client,
            Message message)
        {
            string help =
                "🎭🔥 أوامر لعبة مزاج\n\n" +

                "!مزاج جديد <النقاط> <عدد الفرق>\n" +
                "مثال: !مزاج جديد 2 4\n\n" +

                "!مزاج انضم <الفريق>\n" +
                "🔴 احمر\n" +
                "🔵 ازرق\n" +
                "🟡 اصفر\n" +
                "🟣 بنفسجي\n\n" +

                "!مزاج تغيير <الفريق>\n" +
                "!مزاج لاعبين\n" +
                "!مزاج بدء\n" +
                "!مزاج انهاء\n" +
                "!مزاج بطاقات\n" +
                "!مزاج مساعدة\n\n" +

                "🎴 بعد بدء اللعبة:\n" +
                "اكتب رقم البطاقة فقط مثل:\n" +
                "13\n\n" +

                "⚠️ فقط اللاعب صاحب الدور يستطيع اختيار الرقم.\n" +
                "⏱️ لكل لاعب 25 ثانية.";

            await message.Reply(
                client,
                help
            );
        }

        private static string BuildFinalResults()
        {
            if (_game == null)
                return "";

            string text =
                "🏆 النتائج النهائية\n\n";

            List<Team> teams =
                _game.Teams
                    .OrderByDescending(t => t.Score)
                    .ToList();

            for (int i = 0; i < teams.Count; i++)
            {
                Team team = teams[i];

                string position = i switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    2 => "🥉",
                    _ => "🏅"
                };

                text +=
                    $"{position} {team.Emoji} {team.Name}: " +
                    $"{team.Score} نقطة\n";
            }

            return text.TrimEnd();
        }

        private static string NormalizeTeam(
            string input)
        {
            string value =
                input.Trim().ToLowerInvariant();

            return value switch
            {
                "احمر" => "احمر",
                "أحمر" => "احمر",
                "red" => "احمر",

                "ازرق" => "ازرق",
                "أزرق" => "ازرق",
                "blue" => "ازرق",

                "اصفر" => "اصفر",
                "أصفر" => "اصفر",
                "yellow" => "اصفر",

                "بنفسجي" => "بنفسجي",
                "purple" => "بنفسجي",

                _ => ""
            };
        }

        private static bool TryParseNumber(
            string text,
            out int number)
        {
            number = 0;

            return int.TryParse(
                text.Trim(),
                out number
            );
        }

        private static string FormatValue(
            int value)
        {
            if (value > 0)
                return $"+{value}";

            return value.ToString();
        }

        private static async Task<string> GetNickname(
            IWolfClient client,
            string userId)
        {
            try
            {
                User? user =
                    await client.GetUser(userId);

                if (user != null &&
                    !string.IsNullOrWhiteSpace(user.Nickname))
                {
                    return user.Nickname;
                }
            }
            catch
            {
                // استخدام ID إذا تعذر جلب الاسم
            }

            return userId;
        }
    }

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

        public string GroupId { get; set; }

        public MazajGame(
            int pointsPerCard,
            int teamCount)
        {
            PointsPerCard = pointsPerCard;
            TeamCount = teamCount;

            Teams = new List<Team>();
            Cards = new List<Card>();
            TurnOrder = new List<string>();

            Started = false;
            CurrentPlayerIndex = 0;
            TurnVersion = 0;
            GroupId = "";

            CreateTeams();
            CreateCards();
        }

        private void CreateTeams()
        {
            string[] names =
            {
                "احمر",
                "ازرق",
                "اصفر",
                "بنفسجي"
            };

            string[] emojis =
            {
                "🔴",
                "🔵",
                "🟡",
                "🟣"
            };

            for (int i = 0; i < TeamCount; i++)
            {
                Teams.Add(
                    new Team(
                        names[i],
                        emojis[i]
                    )
                );
            }
        }

        public string CurrentPlayerId
        {
            get
            {
                if (TurnOrder.Count == 0)
                    return "";

                if (CurrentPlayerIndex < 0 ||
                    CurrentPlayerIndex >= TurnOrder.Count)
                {
                    return "";
                }

                return TurnOrder[
                    CurrentPlayerIndex
                ];
            }
        }

        public string CurrentPlayerName
        {
            get
            {
                string playerId =
                    CurrentPlayerId;

                if (string.IsNullOrWhiteSpace(playerId))
                    return "غير معروف";

                Team? team =
                    GetTeamByPlayer(playerId);

                if (team != null &&
                    team.Players.TryGetValue(
                        playerId,
                        out string? nickname))
                {
                    return nickname;
                }

                return playerId;
            }
        }

        public Team? GetTeamByPlayer(
            string playerId)
        {
            return Teams.FirstOrDefault(
                team => team.Players.ContainsKey(playerId)
            );
        }

        public bool AllCardsUsed
        {
            get
            {
                return Cards.All(
                    card => card.Used
                );
            }
        }

        private void CreateCards()
        {
            Cards.Clear();

            string[] names =
            {
                "ضربة قوية",
                "ضربة سند سوريا",
                "هدية",
                "خسارة",
                "ربح",
                "مضاعفة",
                "مفاجأة",
                "حظ سعيد",
                "حظ عاثر",
                "تحدي",
                "نقطة إضافية",
                "خصم",
                "ضربة",
                "فرصة",
                "كنز",
                "مكافأة",
                "عقوبة",
                "عودة",
                "هجوم",
                "دفاع"
            };

            Random random = new Random();

            for (int i = 1; i <= 65; i++)
            {
                string name =
                    names[random.Next(names.Length)];

                int value =
                    random.Next(
                        -PointsPerCard * 5,
                        PointsPerCard * 6
                    );

                if (value == 0)
                    value = PointsPerCard;

                Cards.Add(
                    new Card(
                        i,
                        name,
                        value
                    )
                );
            }

            // نخلي "ضربة سند سوريا" مرة واحدة فقط
            Card? specialCard =
                Cards.FirstOrDefault(
                    c => c.Name == "ضربة سند سوريا"
                );

            if (specialCard == null)
            {
                Cards[0].Name =
                    "ضربة سند سوريا";
            }

            bool firstFound = false;

            foreach (Card card in Cards)
            {
                if (card.Name == "ضربة سند سوريا")
                {
                    if (!firstFound)
                    {
                        firstFound = true;
                    }
                    else
                    {
                        card.Name =
                            "ضربة قوية";
                    }
                }
            }
        }
    }

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
            Score = 0;

            Players =
                new Dictionary<string, string>();
        }
    }

    public class Card
    {
        public int Number { get; }

        public string Name { get; set; }

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
