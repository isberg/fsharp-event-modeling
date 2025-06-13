# CategoryApp Sample

This sample demonstrates category-level projections. It exposes views for each counter as well as aggregated views across the whole `Counter` category.

Run the sample with:

```bash
dotnet run --project samples/CategoryApp/CategoryApp.fsproj
```

Try the following commands:

```bash
# increment two counters
curl localhost:8080/counter/1 -d '"Increment"'
curl localhost:8080/counter/2 -d '"Increment"'

# view individual counts
curl localhost:8080/counters/1/count
curl localhost:8080/counters/2/count

# view all counts and the total
curl localhost:8080/counters/all
curl localhost:8080/counters/total
```
