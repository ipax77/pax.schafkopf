using System;
using System.Linq;
using System.Text.Json;

namespace pax.schafkopf.lib
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            //var random = new Random();
            //SKTable table = new SKTable();
            //for (int h = 0; h < 100; h++)
            //{
            //    table.Bidding1(false);
            //    table.Bidding1(false);
            //    table.Bidding1(false);
            //    table.Bidding1(true);
            //    table.Bidding2((int)Enums.GameMode.Solo, (int)Enums.Suit.Herz, 0);
            //    for (int j = 0; j < 8; j++)
            //    {
            //        for (int i = 0; i < 4; i++)
            //        {
            //            var validcards = table.Players[table.CurrentPlayer].ValidCards(table.Players[table.LeadingPlayer].TrickCard, table).ToList();
            //            var card = validcards[random.Next(0, validcards.Count())];
            //            table.PlayCard(card.Rank, card.Suit);
            //        }
            //    }

            //    //for (int i = 0; i < 4; i++)
            //    //{
            //    //    Console.WriteLine($"{i}: {table.Players[i].Tricks.Sum(s => s.GetValue())}");
            //    //}

            //    Console.WriteLine($"{table.Round}: Value: {table.GameValue}, Runners: {table.Runners}, Points: {table.PlayerPoints}");
            //    table.NextRound();
            //}
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            SKTable table = new SKTable(false);
            Console.WriteLine($"{table.LeadingPlayer} {table.Guid}");
            var json = JsonSerializer.Serialize(table, options);
            SKTable table2 = JsonSerializer.Deserialize<SKTable>(json, options);
            Console.WriteLine($"{table2.LeadingPlayer} {table2.Guid}");

            SKPlayer player = new SKPlayer() { Position = 2 };
            var pljson = JsonSerializer.Serialize(player);
            SKPlayer player2 = JsonSerializer.Deserialize<SKPlayer>(pljson);
            Console.WriteLine(player2.Position);

        }
    }
}

