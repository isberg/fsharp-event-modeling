# ViewApp Sample

This sample builds on the basic counter domain and demonstrates the **View Pattern**. It exposes two projections via HTTP using `GenericResource.configure` with a `ResourceConfig`:

- `count` – the current numeric value of the counter
- `history` – the full list of events applied to the counter

Run the sample with:

```bash
dotnet run --project samples/ViewApp/ViewApp.fsproj
```

Then retrieve views, for example:

```bash
# send a command
curl localhost:8080/counter/1 -d '"Increment"'

# view the current count
curl localhost:8080/counter/1/count

# view event history
curl localhost:8080/counter/1/history
```
