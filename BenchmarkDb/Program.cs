using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

using MySql.Data.MySqlClient;
using Npgsql;

namespace BenchmarkDb
{
    class Program
    {
        static volatile int Counter = 0;
        static int NumTasks = 16;

        const string PostgreSql = nameof(PostgreSql);
        const string MySql = nameof(MySql);
        const string SqlServer = nameof(SqlServer);

        static object synlock = new object();

        static int _stopping = 0;
        public static bool Stopping { get => _stopping == 1; set => Interlocked.Exchange(ref _stopping, value ? 1 : 0); }

        static void Main(string[] args)
        {
            System.Net.ServicePointManager.DefaultConnectionLimit = 1000;

            if (args.Length < 2)
            {
                Console.WriteLine("usage: database connectionstring [minTasks [maxTasks]]");
                Environment.Exit(1);
            }

            if (args.Length > 2)
            {
                NumTasks = int.Parse(args[2]);
            }

            var connectionString = args[1];

            DbProviderFactory factory = null;

            switch (args[0])
            {
                case PostgreSql:
                    factory = NpgsqlFactory.Instance;
                    break;

                case MySql:
                    factory = MySqlClientFactory.Instance;
                    break;

                case SqlServer:
                    factory = SqlClientFactory.Instance;
                    break;

                default:
                    Console.WriteLine($"Accepted database values: {SqlServer}, {MySql}, {PostgreSql}");
                    Environment.Exit(2);
                    break;
            }

            var mode = args[3] == "async" ? "async" : "sync";
            var delay = int.Parse(args[4]) * 1000;

            Console.WriteLine($"Running with {args[0]} on {connectionString}, {mode} mode");

            var stopwatch = new Stopwatch();
            var startTime = DateTime.UtcNow;
            var stopTime = DateTime.UtcNow;
            var lastDisplay = DateTime.UtcNow;
            var lastNewTask = DateTime.UtcNow;
            var totalTransactions = 0;
            var listOfResults = new List<double>(1000);
            var lotsofbackspaces = String.Join("", Enumerable.Repeat("\b", 80));
            var lotsofspaces = String.Join("", Enumerable.Repeat(" ", 80));

            var tasks = mode == "async" ?
               Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWork, TaskCreationOptions.LongRunning)).ToList() :
               Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWorkAsync, TaskCreationOptions.LongRunning).Unwrap()).ToList();

            Task.Run(async () =>
            {
                while (!Stopping)
                {
                    await Task.Delay(1000);
                    var now = DateTime.UtcNow;
                    var tps = Counter / (now - lastDisplay).TotalSeconds;
                    totalTransactions += Counter;
                    listOfResults.Add(tps);
                    Console.Write(lotsofbackspaces);
                    Console.Write($"{now.TimeOfDay}: {tasks.Count:D2} Threads, tps: {tps:F2}");
                    lastDisplay = now;
                    Interlocked.Exchange(ref Counter, 0);
                }
                
            });

            Task.Run(async () =>
            {
                await Task.Delay(delay);
                Stopping = true;
                stopTime = DateTime.UtcNow;
                await Task.Delay(2000);
            });

            Task.WhenAll(tasks).GetAwaiter().GetResult();
            var totalTps = totalTransactions / (stopTime - startTime).TotalSeconds;
            listOfResults.Remove(listOfResults.Max());
            listOfResults.Remove(listOfResults.Min());
            
            Console.Write(lotsofbackspaces);
            Console.Write(lotsofspaces);
            Console.Write(lotsofbackspaces);
            Console.WriteLine($"{tasks.Count:D2} Threads, tps: {totalTps:F2}, stddev(w/o best+worst): {CalculateStdDev(listOfResults, listOfResults.Count):F2}");
            double CalculateStdDev(IEnumerable<double> values, int count) {
                var avg = values.Sum() / count;
                double sum = values.Sum(d => Math.Pow(d - avg, 2));
                return Math.Sqrt((sum)/count);
            }

 
            async Task DoWorkAsync()
            {
                while (!Stopping)
                {
                    Interlocked.Add(ref Counter, 1);

                    try
                    {
                        var results = new List<Fortune>();

                        using (var connection = factory.CreateConnection())
                        {
                            var command = connection.CreateCommand();
                            command.CommandText = "SELECT id,message FROM fortune";

                            connection.ConnectionString = connectionString;
                            await connection.OpenAsync();

                            command.Prepare();

                            using (var reader = await command.ExecuteReaderAsync())
                            {
                                while (await reader.ReadAsync())
                                {
                                    results.Add(new Fortune
                                    {
                                        Id = reader.GetInt32(0),
                                        Message = reader.GetString(1)
                                    });
                                }
                            }
                        }

                        if (results.Count() != 12)
                        {
                            throw new ApplicationException("Not 12");
                        }

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }


            void DoWork()
            {
                while (!Stopping)
                {
                    Interlocked.Add(ref Counter, 1);

                    try
                    {
                        var results = new List<Fortune>();

                        using (var connection = factory.CreateConnection())
                        {
                            var command = connection.CreateCommand();
                            command.CommandText = "SELECT id,message FROM fortune";

                            connection.ConnectionString = connectionString;
                            connection.Open();

                            command.Prepare();

                            using (var reader = command.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    results.Add(new Fortune
                                    {
                                        Id = reader.GetInt32(0),
                                        Message = reader.GetString(1)
                                    });
                                }
                            }
                        }

                        if (results.Count() != 12)
                        {
                            throw new ApplicationException("Not 12");
                        }

                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }
        }

        
    }

    //class Program
    //{
    //    static volatile int Counter = 0;
    //    static int MinTasks = 16;
    //    static int MaxTasks = 16;

    //    const string PostgreSql = nameof(PostgreSql);
    //    const string MySql = nameof(MySql);
    //    const string SqlServer = nameof(SqlServer);

    //    static object synlock = new object();

    //    static void Main(string[] args)
    //    {
    //        System.Net.ServicePointManager.DefaultConnectionLimit = 60;

    //        if (args.Length < 2)
    //        {
    //            Console.WriteLine("usage: database connectionstring [minTasks [maxTasks]]");
    //            Environment.Exit(1);
    //        }

    //        if (args.Length > 2)
    //        {
    //            MinTasks = MaxTasks = int.Parse(args[2]);
    //        }

    //        if (args.Length > 3)
    //        {
    //            MaxTasks = int.Parse(args[3]);
    //        }

    //        var connectionString = args[1];

    //        DbProviderFactory factory = null;

    //        switch (args[0])
    //        {
    //            case PostgreSql:
    //                factory = NpgsqlFactory.Instance;
    //                break;

    //            case MySql:
    //                factory = MySqlClientFactory.Instance;
    //                break;

    //            case SqlServer:
    //                factory = SqlClientFactory.Instance;
    //                break;

    //            default:
    //                Console.WriteLine($"Accepted database values: {SqlServer}, {MySql}, {PostgreSql}");
    //                Environment.Exit(2);
    //                break;
    //        }

    //        Console.WriteLine($"Running with {args[0]} on {connectionString}");

    //        var stopwatch = new Stopwatch();
    //        var tasks = new List<Task>();
    //        var stopping = false;
    //        var startTime = DateTime.UtcNow;
    //        var lastDisplay = DateTime.UtcNow;
    //        var lastNewTask = DateTime.UtcNow;

    //        while (!stopping)
    //        {
    //            Thread.Sleep(200);
    //            var now = DateTime.UtcNow;

    //            if ((now - lastDisplay) > TimeSpan.FromMilliseconds(200))
    //            {
    //                Console.Write($"{tasks.Count} Threads, {Counter / (now - lastDisplay).TotalSeconds} tps                                   ");
    //                Console.CursorLeft = 0;
    //                lastDisplay = now;
    //                Counter = 0;

    //            }

    //            if ((now - lastNewTask) > TimeSpan.FromMilliseconds(2000))
    //            {
    //                for (int i = tasks.Count; i < MinTasks; i++)
    //                {
    //                    tasks.Add(Task.Run(Thing));
    //                }

    //                if (tasks.Count <= MaxTasks)
    //                {
    //                    tasks.Add(Task.Run(Thing));
    //                }                   

    //                async Task Thing()
    //                {
    //                    while (!stopping)
    //                    {
    //                        Interlocked.Add(ref Counter, 1);

    //                        try
    //                        {
    //                            var results = new List<Fortune>();

    //                            using (var connection = factory.CreateConnection())
    //                            {
    //                                var command = connection.CreateCommand();
    //                                command.CommandText = "SELECT id,message FROM fortune";

    //                                connection.ConnectionString = connectionString;
    //                                await connection.OpenAsync();

    //                                command.Prepare();

    //                                using (var reader = await command.ExecuteReaderAsync())
    //                                {
    //                                    while (await reader.ReadAsync())
    //                                    {
    //                                        results.Add(new Fortune
    //                                        {
    //                                            Id = reader.GetInt32(0),
    //                                            Message = reader.GetString(1)
    //                                        });
    //                                    }
    //                                }
    //                            }

    //                            if (results.Count() != 12)
    //                            {
    //                                throw new ApplicationException("Not 12");
    //                            }

    //                        }
    //                        catch (Exception e)
    //                        {
    //                            Console.WriteLine(e);
    //                        }
    //                    }
    //                }

    //                lastNewTask = now;
    //            }
    //        }

    //        Task.WhenAll(tasks).GetAwaiter().GetResult();            
    //    }
    //}

    public class Fortune
    {
        public int Id { get; set; }
        public string Message { get; set; }
    }
}
