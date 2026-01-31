using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace pax.schafkopf.lib
{
    public class SKTable
    {
        [JsonInclude]public Guid Guid { get; private set; }
        [JsonInclude]public bool isClient { get; set; }
        [JsonInclude]public ushort Round { get; private set; } = 0;
        [JsonInclude]public byte StartingPlayer { get; private set; }
        [JsonInclude]public byte LeadingPlayer { get; private set; }
        [JsonInclude]public byte CurrentPlayer { get; private set; }
        [JsonInclude]public byte PlayingPlayer { get; private set; }
        [JsonInclude]public byte PlayerPoints { get; private set; }
        [JsonInclude]public byte Runners { get; private set; }
        [JsonInclude]public int GameValue { get; private set; }
        [JsonInclude]public byte State { get; private set; }
        [JsonInclude]public byte TrickCount { get; private set; }
        [JsonInclude]public int PlayerCount { get; private set; }
        [JsonInclude]public bool hasMultiplePlayers { get; private set; } = false;
        [JsonInclude]public byte GameMode { get; private set; } = (byte)Enums.GameMode.Ruf;
        [JsonInclude]public byte Trump { get; private set; } = (byte)Enums.Suit.Herz;
        [JsonInclude]public byte Partner { get; private set; }
        [JsonInclude]public SKConfig Config { get; private set; } = new SKConfig();
        [JsonInclude]public SKCard[] LastTrick { get; private set; } = new SKCard[4] { null, null, null, null };
        [JsonInclude]

        public SKPlayer[] Players { get; private set; } = new SKPlayer[4] { new SKPlayer() { Position = 0 },
                                                                    new SKPlayer() { Position = 1 },
                                                                    new SKPlayer() { Position = 2 },
                                                                    new SKPlayer() { Position = 3 }
                                                                  };


        public SKTable() { }

        public SKTable(bool isClient) : this ()
        {
            if (isClient == false)
                this.Guid = Guid.NewGuid();
            this.isClient = isClient;
            StartingPlayer = (byte)(new Random()).Next(0, 4);
            LeadingPlayer = StartingPlayer;
            CurrentPlayer = StartingPlayer;
            State = (int)Enums.TableState.Bidding;
            SK.AddTable(this);
        }

        public void Init()
        {
            SK.AddTable(this);
        }

        public void InitClient(SKConfig config, Guid guid, int startingPlayer)
        {
            this.Guid = guid;
            this.Config = config;
            StartingPlayer = (byte)startingPlayer;
            LeadingPlayer = StartingPlayer;
            CurrentPlayer = StartingPlayer;
            Init();
        }

        public void NextRound()
        {
            StartingPlayer = (byte)((StartingPlayer + 1) % 4);
            LeadingPlayer = StartingPlayer;
            CurrentPlayer = StartingPlayer;
            State = (byte)Enums.TableState.Bidding;
            TrickCount = 0;
            PlayerCount = 0;
            hasMultiplePlayers = false;
            GameMode = (byte)Enums.GameMode.Weiter;
            Trump = (byte)Enums.Suit.Herz;
            Partner = (byte)Enums.Suit.Herz;
            LastTrick = new SKCard[4] { null, null, null, null };
            PlayerPoints = 0;
            Runners = 0;
            GameValue = 0;
            Round++;
            for (int i = 0; i < 4; i++)
                Players[i].Reset();
            DealCards();
        }

        public bool PlayerMoved(int pos)
        {
            return SK.PlayerDistance(LeadingPlayer, pos) < SK.PlayerDistance(LeadingPlayer, CurrentPlayer);
        }

        public void NextPlayer()
        {
            if (State == (byte)Enums.TableState.Bidding2)
            {
                for (int i = 1; i < 4; i++)
                {
                    if (Players[(CurrentPlayer + i) % 4].isPlaying)
                    {
                        CurrentPlayer = (byte)((CurrentPlayer + i) % 4);
                        break;
                    }
                }
            } else
                CurrentPlayer = (byte)((CurrentPlayer + 1) % 4);
        }

        public void DealCards()
        {
            if (isClient)
                return;
            Random random = new Random();
            var myDeck = (SKCard[])SK.Deck.Clone();
            for (int i = 32 - 1; i >= 0; i--)
            {
                int index = random.Next(0, i + 1);
                var temp = myDeck[i];
                myDeck[i] = myDeck[index];
                myDeck[index] = temp;
            }
            for (int i = 0; i < 4; i++)
                Players[i].Cards = myDeck.Skip(i * 8).Take(8).ToList();
            SortHands();
        }

        public void SortHands()
        {
            for (int i = 0; i < 4; i++)
            {
                if (Players[i].Cards != null && Players[i].Cards.Any())
                    Players[i].Cards = Players[i].Cards.OrderByDescending(o => o.GetCardOrder(GameMode, Trump)).ToList();
            }
        }

        public void Bidding1(bool isPlaying)
        {
            Players[CurrentPlayer].isPlaying = isPlaying;
            if (isPlaying)
            {
                PlayerCount++;
                if (PlayerCount > 1)
                    hasMultiplePlayers = true;
            }
            NextPlayer();
            if (CurrentPlayer == LeadingPlayer)
                if (PlayerCount > 0)
                {
                    State = (byte)Enums.TableState.Bidding2;
                    if (!Players[CurrentPlayer].isPlaying)
                        NextPlayer();
                }
                else
                    State = (byte)Enums.TableState.Finished;
        }

        public void Bidding2(int mode, int trump, int partner)
        {
            if (mode == (int)Enums.GameMode.Weiter)
            {
                Players[CurrentPlayer].isPlaying = false;
                PlayerCount--;
            } else
            {
                Players[CurrentPlayer].GameProposal = (byte)mode;
                Players[CurrentPlayer].TrumpProposal = (byte)trump;
                Players[CurrentPlayer].Partner = (byte)partner;
                GameMode = Players[CurrentPlayer].GameProposal;
                Trump = Players[CurrentPlayer].TrumpProposal;
                Partner = Players[CurrentPlayer].Partner;
            }
            if (PlayerCount == 1)
            {
                if (Players[CurrentPlayer].isPlaying)
                    PlayingPlayer = (byte)CurrentPlayer;
                else
                {
                    byte npos = Players.Single(s => s.isPlaying).Position;
                    if (!PlayerMoved(npos))
                    {
                        NextPlayer();
                        return;
                    }
                    else
                        PlayingPlayer = npos;

                }
            }
            else if (PlayerCount > 1)
            {
                var playersMoved = Players.Where(x => x.isPlaying).Select(s => PlayerMoved(s.Position));
                if (playersMoved.Count(c => c == false) == 1)
                {
                    PlayingPlayer = CurrentPlayer;
                    foreach (var pl in Players.Where(x => x.isPlaying && x.Position != PlayingPlayer))
                        pl.isPlaying = false;
                }
                else
                {
                    NextPlayer();
                    return;
                }
            } else
            {
                throw new InvalidOperationException("one player has to play.");
            }
            SortHands();
            CurrentPlayer = LeadingPlayer;
            State = (byte)Enums.TableState.Playing;
        }

        public List<int> GetValidBidModes(int playerPos)
        {
            if (Players[playerPos].Cards != null && Players[playerPos].Cards.Where(x => x.Rank == (int)Enums.Rank.Ober).Count() == 4 && Players[playerPos].Cards.Where(x => x.Rank == (int)Enums.Rank.Unter).Count() == 4)
                return new List<int>() { (int)Enums.GameMode.Sie };

            List<int> validBids = new List<int>()
            {
                (int)Enums.GameMode.Ruf,
                (int)Enums.GameMode.Wenz,
                (int)Enums.GameMode.Solo,
                (int)Enums.GameMode.WenzTout,
                (int)Enums.GameMode.SoloTout
            };

            if (hasMultiplePlayers)
                validBids.Remove((int)Enums.GameMode.Ruf);

            for (int i = 1; i < Enum.GetValues(typeof(Enums.GameMode)).Length; i++)
                if (GameMode >= i)
                    validBids.Remove(i);
            if (PlayerCount > 1)
            {
                validBids.Add((int)Enums.GameMode.Weiter);
            }
            return validBids;
        }

        public List<int> GetValidSuits(int playerPos, int gameMode)
        {
            List<int> validSuits = new List<int>()
            {
                (int)Enums.Suit.Eichel,
                (int)Enums.Suit.Gras,
                (int)Enums.Suit.Herz,
                (int)Enums.Suit.Schellen,
            };
            if (gameMode == (int)Enums.GameMode.Ruf)
            {
                validSuits.Remove((int)Enums.Suit.Herz);
                if (Players[playerPos].Cards != null)
                {
                    foreach (var suit in validSuits.ToArray())
                    {
                        if (Players[playerPos].Cards.SingleOrDefault(s => s.Suit == suit && s.Rank == (int)Enums.Rank.Ace) != null)
                            validSuits.Remove(suit);
                        else if (!Players[playerPos].Cards.Where(x => x.Rank != (int)Enums.Rank.Ober && x.Rank != (int)Enums.Rank.Unter && x.Suit == suit).Any())
                            validSuits.Remove(suit);
                    }
                }
            } else if (gameMode == (int)Enums.GameMode.Wenz || gameMode == (int)Enums.GameMode.WenzTout)
            {
                return new List<int>() { (int)Enums.Suit.Farblos };
            }
            return validSuits;
        }

        public void PlayCard(int rank, int suit)
        {
            if (Players[CurrentPlayer].Cards.Any())
                Players[CurrentPlayer].Cards.Remove(Players[CurrentPlayer].Cards.Single(s => s.Rank == rank && s.Suit == suit));
            Players[CurrentPlayer].TrickCard = new SKCard() { Rank = (byte)rank, Suit = (byte)suit };
            Players[CurrentPlayer].CardsPlayed.Add(Players[CurrentPlayer].TrickCard.Copy());
            
            if (Players.Where(x => x.TrickCard != null).Count() == 4)
                CollectTrick();
            else
                NextPlayer();
        }

        public IEnumerable<SKCard> GetValidCards(int playerPos)
        {
            return Players[playerPos].ValidCards(Players[LeadingPlayer].TrickCard, this);
        }

        private void CollectTrick()
        {
            SKCard firstCard = Players[LeadingPlayer].TrickCard;
            SKCard winnerCard = firstCard;
            int winnerIndex = LeadingPlayer;
            LastTrick[LeadingPlayer] = Players[LeadingPlayer].TrickCard.Copy();
            for (int i = 0; i < 4; i++)
            {
                int nextindex = (LeadingPlayer + i) % 4;
                LastTrick[nextindex] = Players[nextindex].TrickCard.Copy();
                SKCard nextCard = Players[nextindex].TrickCard;
                if (winnerCard.GetCardRank(GameMode, Trump, firstCard) < nextCard.GetCardRank(GameMode, Trump, firstCard))
                {
                    winnerCard = nextCard;
                    winnerIndex = nextindex;
                }
            }
            Players[winnerIndex].Tricks.AddRange(Players.Select(s => s.TrickCard.Copy()));

            if (GameMode == (int)Enums.GameMode.Ruf && !Players.Where(x => x.isPartner).Any())
                SetPartner();

            TrickCount++;
            if (TrickCount == 8)
                RoundEnd();
            else
            {
                Array.ForEach(Players, s => s.TrickCard = null);
                LeadingPlayer = (byte)winnerIndex;
                CurrentPlayer = LeadingPlayer;
            }
        }

        private void SetPartner()
        {
            if (LastTrick[LeadingPlayer].Suit == Players[PlayingPlayer].Partner && LastTrick[LeadingPlayer].Rank != (int)Enums.Rank.Ober && LastTrick[LeadingPlayer].Rank != (int)Enums.Rank.Unter) {
                var rufsau = LastTrick.FirstOrDefault(x => x.Rank == (int)Enums.Rank.Ace && x.Suit == Players[PlayingPlayer].Partner);
                if (rufsau != null)
                    Players[Array.IndexOf(LastTrick, rufsau)].isPartner = true;
                else
                    Players[LeadingPlayer].isPartner = true;
            }
        }

        private void RoundEnd()
        {
            List<SKPlayer> players = new List<SKPlayer>() { Players[PlayingPlayer] };
            if (GameMode == (byte)Enums.GameMode.Ruf)
                players.Add(Players.Single(s => s.isPartner));
            PlayerPoints = (byte)players.SelectMany(s => s.Tricks).Sum(u => u.GetValue());
            bool playerswin = ((Enums.GameMode)GameMode, PlayerPoints) switch
            {
                (Enums.GameMode.SoloTout, 120) => true,
                (Enums.GameMode.WenzTout, 120) => true,
                (Enums.GameMode.Ruf, > 60) => true,
                (Enums.GameMode.Wenz, > 60) => true,
                (Enums.GameMode.Solo, > 60) => true,
                (Enums.GameMode.Sie, 120) => true,
                _ => false
            };

            GameValue = SetGameValue(players);

            for (int i = 0; i < 4; i++)
            {
                if (players.Contains(Players[i]))
                {
                    Players[i].isWinner = playerswin;
                    Players[i].isPlayer = true;
                    if (playerswin)
                        if (players.Count == 1)
                            Players[i].Points += 3 * GameValue;
                        else
                            Players[i].Points += GameValue;
                    else
                        if (players.Count == 1)
                        Players[i].Points -= 3 * GameValue;
                    else
                        Players[i].Points -= GameValue;
                }
                else
                {
                    Players[i].isWinner = !playerswin;
                    if (playerswin)
                        Players[i].Points -= GameValue;
                    else
                        Players[i].Points += GameValue;
                }
            }
            State = (byte)Enums.TableState.Finished;
        }

        private int SetGameValue(List<SKPlayer> players)
        {
            int gamevalue = GameMode switch
            {
                (byte)Enums.GameMode.Ruf => Config.RufValue,
                _ => Config.SoloValue
            };
            if (GameMode != (byte)Enums.GameMode.SoloTout || GameMode != (byte)Enums.GameMode.WenzTout || GameMode != (byte)Enums.GameMode.Sie)
            {
                if (PlayerPoints < 30 || PlayerPoints > 90)
                    gamevalue += Config.AddValue;
                if (PlayerPoints == 0 || PlayerPoints == 120)
                    gamevalue += Config.AddValue;
            }

            var playerscards = players.SelectMany(s => s.CardsPlayed);
            SetRunners(playerscards);
            if (Runners == 0)
            {
                var opponents = Players.Where(x => !players.Contains(x));
                var opponentcards = opponents.SelectMany(s => s.CardsPlayed);
                SetRunners(opponentcards);
            }
            if (Runners >= 3 || (GameMode == (byte)Enums.GameMode.Wenz && Runners >= 2))
                gamevalue += Runners * Config.AddValue;

            if (GameMode == (byte)Enums.GameMode.WenzTout || GameMode == (byte)Enums.GameMode.SoloTout)
                gamevalue *= 2;

            else if (GameMode == (byte)Enums.GameMode.Sie)
                gamevalue *= 4;

            return gamevalue;
        }

        private void SetRunners(IEnumerable<SKCard> cards)
        {
            if (GameMode == (byte)Enums.GameMode.Wenz)
            {
                foreach (var card in SK.Deck.Where(x => x.Rank == (byte)Enums.Rank.Unter).OrderBy(o => o.Suit))
                {
                    if (cards.FirstOrDefault(f => f.Rank == card.Rank && f.Suit == card.Suit) != null)
                        Runners++;
                    else
                        break;
                }
            }
            else
            {
                foreach (var card in SK.Deck.Where(x => x.Rank == (byte)Enums.Rank.Ober).OrderByDescending(o => o.Suit))
                {
                    if (cards.FirstOrDefault(f => f.Rank == card.Rank && f.Suit == card.Suit) != null)
                        Runners++;
                    else
                        break;
                }
                if (Runners == 4)
                    foreach (var card in SK.Deck.Where(x => x.Rank == (byte)Enums.Rank.Unter).OrderByDescending(o => o.Suit))
                    {
                        if (cards.FirstOrDefault(f => f.Rank == card.Rank && f.Suit == card.Suit) != null)
                            Runners++;
                        else
                            break;
                    }
            }


        }
    }
}
