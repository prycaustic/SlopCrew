using Serilog;
using Serilog.Core;
using SlopCrew.Common;
using SlopCrew.Common.Network.Clientbound;
using EmbedIO;
using Constants = SlopCrew.Common.Constants;

namespace SlopCrew.Server;

public class Server {
    public static Server Instance = new();
    public static Logger Logger = null!;
    public static uint CurrentTick;

    private string interfaceStr;
    private bool debug;

    public WebServer WebServer;
    public SlopWebSocketModule Module;

    public Server() {
        this.interfaceStr = Environment.GetEnvironmentVariable("SLOP_INTERFACE") ?? "http://+:42069";

        var debugStr = Environment.GetEnvironmentVariable("SLOP_DEBUG")?.Trim().ToLower();
        this.debug = int.TryParse(debugStr, out var debugInt) ? debugInt != 0 : debugStr == "true";

        this.Module = new SlopWebSocketModule();
        this.WebServer = new WebServer(o => {
            o.WithUrlPrefix(this.interfaceStr);
            o.WithMode(HttpListenerMode.EmbedIO);
            // TODO: SSL support again (sorry :/)
        }).WithModule(this.Module);

        var logger = new LoggerConfiguration().WriteTo.Console();
        if (this.debug) logger = logger.MinimumLevel.Verbose();
        Logger = logger.CreateLogger();
        Log.Logger = Logger;
    }

    public void Start() {
        Log.Information("Listening on {Interface} - press any key to close", this.interfaceStr);

        // ReSharper disable once FunctionNeverReturns
        new Thread(() => {
            const int tickRate = (int) (Constants.TickRate * 1000);
            while (true) {
                Thread.Sleep(tickRate);
                this.RunTick();
            }
        }).Start();

        this.WebServer.Start();
    }

    private void RunTick() {
        // Go through each connection and run their respective ticks.
        foreach (var connection in this.GetConnections()) {
            connection.RunTick();
        }

        // Increment the global tick counter.
        CurrentTick++;

        // Send the sync message every 50 ticks.
        if (CurrentTick % 50 == 0) {
            SendSyncToAllConnections(CurrentTick);
        }
    }

    private void SendSyncToAllConnections(uint tick) {
        var syncMessage = new ClientboundSync {
            ServerTickActual = tick
        };
        var serialized = syncMessage.Serialize();

        foreach (var connection in this.GetConnections()) {
            this.Module.SendToContext(connection.Context, serialized);
        }
    }

    public void TrackConnection(ConnectionState conn) {
        var player = conn.Player;
        if (player is null) {
            Log.Warning("TrackConnection but player is null? {Connection}", conn.DebugName());
            return;
        }

        // Remove from the old stage if crossing into a new one
        if (conn.LastStage != null && conn.LastStage != player.Stage) {
            this.BroadcastNewPlayers(conn.LastStage.Value);
        }

        // ...and broadcast into the new one
        this.BroadcastNewPlayers(player.Stage);
    }

    public void UntrackConnection(ConnectionState conn) {
        var player = conn.Player;

        // Don't bother untracking someone we never tracked in the first place
        if (player is null) return;

        // Contains checks just in case we get into this state somehow
        this.BroadcastNewPlayers(player.Stage, conn);
    }

    private void BroadcastNewPlayers(int stage, ConnectionState? exclude = null) {
        var connections = this.GetConnections()
                              .Where(x => x.Player?.Stage == stage)
                              .ToList();

        // Precalculate this outside the loop and filter out null players
        var allPlayersInStage = connections.Select(c => c.Player)
                                           .Where(p => p != null)
                                           .Cast<Player>()
                                           .ToList();

        foreach (var connection in connections) {
            if (connection != exclude) {
                // This will be a list of all players except for the current one
                var playersToSend = allPlayersInStage
                                    .Where(p => p.ID != connection.Player?.ID)
                                    .ToList();

                this.Module.SendToContext(connection.Context, new ClientboundPlayersUpdate {
                    Players = playersToSend
                });
            }
        }
    }

    public uint GetNextID() {
        var ids = this.GetConnections().Select(x => x.Player?.ID).ToList();
        var id = 0u;
        while (ids.Contains(id)) id++;
        return id;
    }

    public List<ConnectionState> GetConnections() {
        return this.Module.Connections.Values.ToList();
    }
}
