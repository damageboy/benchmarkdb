#!/bin/bash
time=$1
echo Building...
dotnet publish -c release -r ubuntu-x64
cp ../../npgsql.git/src/Npgsql/bin/release/netstandard2.0/Npgsql.{dll,pdb} ./bin/release/netcoreapp2.0/ubuntu-x64/publish/
npgsql_desc=$(cd ../../npgsql.git; git describe --tags HEAD)
echo Running on Npgsql: $npgsql_desc
ls -al ./bin/release/netcoreapp2.0/ubuntu-x64/publish/Npgsql.*

echo Running...
conn="Server=1.1.1.2;Database=bench;Username=benchmarkdbuser;password=bench;NoResetOnClose=true;Maximum Pool Size=200"
echo "|Mode|Threads|TPS|StdDev(w/o best/worst)|" > results.md 
echo "Mode,Threads,TPS,StdDev" > results.csv
echo "|----|-------|---|------|" >> results.md
for m in sync sync+conn sync+conn+cmd async async+conn async+conn+cmd; do 
  for t in 1 4 8 16 32 64 128; do 
    ./bin/release/netcoreapp2.0/ubuntu-x64/publish/BenchmarkDb PostgreSql "$conn" $t $m $time
  done
done
