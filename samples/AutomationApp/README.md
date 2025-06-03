# AutomationApp Sample

This sample demonstrates the **Automation Pattern**. The counter service
contains a trigger that automatically issues an `Increment` command whenever
the counter value reaches zero.

Run the sample with:

```bash
dotnet run --project samples/AutomationApp/AutomationApp.fsproj
```

Try the following sequence of commands:

```bash
# increment to 1
curl localhost:8080/counters/1 -d '"Increment"'

# decrement back to 0 – automation immediately increments again
curl localhost:8080/counters/1 -d '"Decrement"'

# inspect the current count (should be 1)
curl localhost:8080/counters/1/count
```
