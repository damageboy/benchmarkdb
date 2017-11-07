#!/bin/bash

if [ ! -e ../npgsql ]; then
  echo "Cloning Npgsql for hotpatching"
  git clone git@github.com:npgsql/npgsql.git ../npgsql
fi
(echo "Building Npgsql"; cd ../npgsql/src/Npgsql; dotnet build -c release -f netstandard2.0)
time=$1
echo Building benchmark...
dotnet publish -c release -r ubuntu-x64
cp ../npgsql/src/Npgsql/bin/release/netstandard2.0/Npgsql.{dll,pdb} ./bin/release/netcoreapp2.0/ubuntu-x64/publish/
npgsql_desc=$(cd ../npgsql; git describe --tags HEAD)
echo Running on Npgsql: $npgsql_desc
ls -al ./bin/release/netcoreapp2.0/ubuntu-x64/publish/Npgsql.*

echo Running...
conn="Server=1.1.1.2;Database=bench;Username=benchmarkdbuser;password=bench;NoResetOnClose=true;Maximum Pool Size=200"
echo "|Desc|Mode|Threads|TPS|StdDev(w/o best/worst)|" > results.md 
echo "Desc,Mode,Threads,TPS,StdDev" > results.csv
echo "|-|-|-|-|-|" >> results.md
for m in sync sync+conn sync+conn+cmd async async+conn async+conn+cmd; do 
  for t in 1 4 8 16 32 64 128; do 
    ./bin/release/netcoreapp2.0/ubuntu-x64/publish/BenchmarkDb PostgreSql "$conn" $t $m $time "$npgsql_desc"
  done
done
