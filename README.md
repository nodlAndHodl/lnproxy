# LnProxy
Basic dotnet web api following the specs identified at the https://github.com/lnproxy/lnproxy-relay and https://github.com/lnproxy/spec. 

### Impetus
From a development perspective I wanted learn more about Hodl Invoices. I saw a demo on the use of ln proxy relays for privacy by Open Noms while I was in the Adopting Bitcoin conference in El Salvador 2022. The idea of a proxy relay was intriguing and took me a while :) to create an implementation of my own.

### Running the APP
No Dockerfile yet, but if you have an install of dotnet 7 run `dotnet build` and `dotnet run`. This will bring up the app with swagger UI at https://localhost:7290/swagger/index.html, or you could do some curl using the following example. 

```
curl --header "Content-Type: application/json" \
    --request POST \
    --data '{"invoice":"<bolt11 invoice>"}' \
    https://localhost:7290
```

I have not yet run this in a production environment, so exposing over tor or production is completely up to you if you run it in that situation. I'll flesh out details in the future if I take this project in that direction.

### Further Ideas
I think something like this would benefit from more infrastructure for monitoring invoice state and recovering after startup/failures. Basically I think there might be an off chance for loss of funds in the event that the invoice that's been wrapped was paid, but the hodl invoice has not yet settled. A small SQL Lite DB might be appropriate and could do a lot for recovery. Just some thoughts at this point.