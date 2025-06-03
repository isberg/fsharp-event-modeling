# TranslationApp Sample

This sample wires two counter services together using the **Translation Pattern**.
Events from the `Counter` category are translated into commands for the
`Mirror` category. In this example only `Incremented` events are mirrored as
`Increment` commands.

Run the sample with:

```bash
dotnet run --project samples/TranslationApp/TranslationApp.fsproj
```

Try the following commands:

```bash
# increment the main counter
curl localhost:8080/counters/1 -d '"Increment"'

# the mirror service has also incremented
curl localhost:8080/mirror/1/count

# decrement only affects the main counter
curl localhost:8080/counters/1 -d '"Decrement"'

# mirror count remains 1
curl localhost:8080/mirror/1/count
```
