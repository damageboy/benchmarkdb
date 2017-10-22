#!/bin/bash
time=$1
echo Building...
dotnet build -c release

echo Running...
conn="Server=1.1.1.2;Database=bench;Username=benchmarkdbuser;password=bench"
echo "|Mode|Threads|TPS|StdDev(w/o best/worst)|" > results.md 
echo "|----|-------|---|------|" >> results.md
for m in sync sync+conn sync+conn+cmd async async+conn async+conn+cmd; do 
  for t in 1 4 8 16 32; do 
    dotnet run --no-build -c release PostgreSql $conn $t $m $time
  done
done
