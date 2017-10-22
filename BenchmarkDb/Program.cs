using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
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
        static int _counter;
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

            var mode = args[3];
            var seconds = int.Parse(args[4]);
            var delay = seconds * 1000;

            Console.WriteLine($"{args[0]}: {mode} mode");

            var stopwatch = new Stopwatch();
            var startTime = DateTime.UtcNow;
            var stopTime = DateTime.UtcNow;
            var lastDisplay = DateTime.UtcNow;
            var lastNewTask = DateTime.UtcNow;
            var totalTransactions = 0;
            var listOfResults = new List<double>(seconds + 60);
            var lotsofbackspaces = String.Join("", Enumerable.Repeat("\b", 80));
            var lotsofspaces = String.Join("", Enumerable.Repeat(" ", 80));

            List<Task> tasks = null;
            switch (mode)
            {
                case "sync":
                    tasks = Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWorkSync, TaskCreationOptions.LongRunning)).ToList();
                    break;
                case "sync+conn":
                    tasks = Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWorkSyncKeepConn, TaskCreationOptions.LongRunning)).ToList();
                    break;
                case "sync+conn+cmd":
                    tasks = Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWorkSyncKeepConnKeepCmd, TaskCreationOptions.LongRunning)).ToList();
                    break;
                case "async":
                    tasks = Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWorkAsync, TaskCreationOptions.LongRunning).Unwrap()).ToList();
                    break;
                case "async+conn":
                    tasks = Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWorkAsyncKeepConn, TaskCreationOptions.LongRunning).Unwrap()).ToList();
                    break;
                case "async+conn+cmd":
                    tasks = Enumerable.Range(1, NumTasks).Select(_ => Task.Factory.StartNew(DoWorkAsyncKeepConnKeepCmd, TaskCreationOptions.LongRunning).Unwrap()).ToList();
                    break;
                default:
                    throw new Exception($"Unknown benchmark mode {mode} requesgted");

            }
            var reporterTask =
                Task.Run(async () => {
                while (!Stopping)
                {
                    await Task.Delay(1000);
                    var now = DateTime.UtcNow;
                    var tps = _counter / (now - lastDisplay).TotalSeconds;
                    totalTransactions += _counter;
                    listOfResults.Add(tps);
                    Console.Write(lotsofbackspaces);
                    Console.Write($"{now.TimeOfDay}: {tasks.Count:D2} Threads, tps: {tps:F2}");
                    lastDisplay = now;
                    Interlocked.Exchange(ref _counter, 0);
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
            reporterTask.GetAwaiter().GetResult();

            var totalTps = totalTransactions / (stopTime - startTime).TotalSeconds;

            listOfResults.Remove(listOfResults.Max());
            listOfResults.Remove(listOfResults.Min());
            var stddev = CalculateStdDev(listOfResults, listOfResults.Count);
            Console.Write(lotsofbackspaces);
            Console.Write(lotsofspaces);
            Console.Write(lotsofbackspaces);
            Console.WriteLine($"{tasks.Count:D2} Threads, tps: {totalTps:F2}, stddev(w/o best+worst): {stddev:F2}");

            using (var sw = File.AppendText("results.md"))
            {
                sw.WriteLine($"|{mode}|{tasks.Count:D2}|{totalTps:F0}|{stddev:F0}|");
            }

            double CalculateStdDev(IEnumerable<double> values, int count)
            {
                var avg = values.Sum() / count;
                double sum = values.Sum(d => Math.Pow(d - avg, 2));
                return Math.Sqrt((sum) / count);
            }


            async Task DoWorkAsync()
            {
                var results = new List<Fortune>();
                while (!Stopping)
                {
                    Interlocked.Add(ref _counter, 1);

                    try
                    {
                        using (var connection = factory.CreateConnection())
                        {
                            connection.ConnectionString = connectionString;
                            await connection.OpenAsync();
                            using (var command = connection.CreateCommand())
                            {
                                command.CommandText = "SELECT id,message FROM fortune";
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
                            results.Clear();
                        }
                    }

                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                } // Stopping
            }


            async Task DoWorkAsyncKeepConn()
            {
                var results = new List<Fortune>();
                using (var connection = factory.CreateConnection())
                {
                    connection.ConnectionString = connectionString;
                    await connection.OpenAsync();
                    while (!Stopping)
                    {
                        Interlocked.Add(ref _counter, 1);

                        try
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.CommandText = "SELECT id,message FROM fortune";
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
                            results.Clear();
                        }

                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    } // Stopping
                } // Connection
            }

            async Task DoWorkAsyncKeepConnKeepCmd()
            {
                var results = new List<Fortune>();
                using (var connection = factory.CreateConnection())
                using (var command = connection.CreateCommand())
                {
                    connection.ConnectionString = connectionString;
                    await connection.OpenAsync();
                    command.CommandText = "SELECT id,message FROM fortune";
                    command.Prepare();
                    while (!Stopping)
                    {
                        Interlocked.Add(ref _counter, 1);

                        try
                        {
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

                            if (results.Count() != 12)
                            {
                                throw new ApplicationException("Not 12");
                            }
                            results.Clear();
                        }

                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    } // Stopping
                }
            }
            void DoWorkSync()
            {
                var results = new List<Fortune>();

                    while (!Stopping)
                    {
                        Interlocked.Increment(ref _counter);

                        try
                        {
                            using (var connection = factory.CreateConnection())
                            {
                                connection.ConnectionString = connectionString;
                                connection.Open();
                                using (var command = connection.CreateCommand())
                                {
                                    command.CommandText = "SELECT id,message FROM fortune";
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
                            }

                            if (results.Count() != 12)
                            {
                                throw new ApplicationException("Not 12");
                            }
                            results.Clear();

                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
            }

            void DoWorkSyncKeepConn()
            {
                var results = new List<Fortune>();
                using (var connection = factory.CreateConnection())
                {
                    connection.ConnectionString = connectionString;
                    connection.Open();

                    while (!Stopping)
                    {
                        Interlocked.Increment(ref _counter);

                        try
                        {
                            using (var command = connection.CreateCommand())
                            {
                                command.CommandText = "SELECT id,message FROM fortune";
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
                            results.Clear();

                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                } // Connection
            }


            void DoWorkSyncKeepConnKeepCmd()
            {
                var results = new List<Fortune>();
                using (var connection = factory.CreateConnection())
                using (var command = connection.CreateCommand())
                {
                    connection.ConnectionString = connectionString;
                    connection.Open();
                    command.CommandText = "SELECT id,message FROM fortune";
                    command.Prepare();
                    while (!Stopping)
                    {
                        Interlocked.Increment(ref _counter);

                        try
                        {
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

                            if (results.Count() != 12)
                            {
                                throw new ApplicationException("Not 12");
                            }
                            results.Clear();

                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                }
            }
        }


    }

    public class Fortune
    {
        public int Id { get; set; }
        public string Message { get; set; }
    }
}
