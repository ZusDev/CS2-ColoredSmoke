using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using System.Text.Json.Serialization;

namespace ColoredSmoke;

public class ConfigGen : BasePluginConfig
{
    [JsonPropertyName("Enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("Color")] public string Color { get; set; } = "random";
    [JsonPropertyName("AdminsOnly")] public bool AdminsOnly { get; set; } = false;
    [JsonPropertyName("AdminFlag")] public string AdminFlag { get; set; } = "@css/generic";
    [JsonPropertyName("SmokeDuration")] public float SmokeDuration { get; set; } = 18.0f;
    [JsonPropertyName("CustomDurationEnabled")] public bool CustomDurationEnabled { get; set; } = false;
}

public partial class ColoreddSmoke : BasePlugin, IPluginConfig<ConfigGen>
{
    public override string ModuleName => "ColoredSmoke";
    public override string ModuleAuthor => "M1k@c";
    public override string ModuleDescription => "ColoredSmoke with custom duration";
    public override string ModuleVersion => "1.0.0";

    public ConfigGen Config { get; set; } = null!;
    public void OnConfigParsed(ConfigGen config) { Config = config; }

    private Dictionary<nint, (CCSPlayerController player, float expireTime)> _activeSmokes = new();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        RegisterEventHandler<EventSmokegrenadeExpired>(OnSmokeExpired);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
        
        AddTimer(1.0f, () =>
        {
            var currentTime = Server.CurrentTime;
            var smokesToRemove = _activeSmokes.Where(x => currentTime >= x.Value.expireTime).ToList();
            
            foreach (var smoke in smokesToRemove)
            {
                _activeSmokes.Remove(smoke.Key);
            }
        }, TimerFlags.REPEAT);
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _activeSmokes.Clear();
        return HookResult.Continue;
    }

    private HookResult OnSmokeExpired(EventSmokegrenadeExpired @event, GameEventInfo info)
    {
        return HookResult.Continue;
    }

    private void OnEntitySpawned(CEntityInstance entity)
    {
        if (entity.DesignerName != "smokegrenade_projectile") return;

        var smokeGrenadeEntity = new CSmokeGrenadeProjectile(entity.Handle);
        if (smokeGrenadeEntity.Handle == IntPtr.Zero) return;

        if (Config.Enabled)
        {
            Server.NextFrame(() =>
            {
                var throwerValue = smokeGrenadeEntity.Thrower.Value;
                if (throwerValue == null) return;

                var throwerValueController = throwerValue.Controller.Value;
                if (throwerValueController == null) return;

                var player = new CCSPlayerController(throwerValueController.Handle);
                if (player == null || !player.IsValid) return;

                if (Config.AdminsOnly && !AdminManager.PlayerHasPermissions(player, Config.AdminFlag))
                {
                    return;
                }

                SetSmokeColor(smokeGrenadeEntity, player);

                if (Config.CustomDurationEnabled && Config.SmokeDuration > 0)
                {
                    ApplyCustomDuration(smokeGrenadeEntity, player);
                }
            });
        }
    }

    private void SetSmokeColor(CSmokeGrenadeProjectile smoke, CCSPlayerController player)
    {
        switch (Config.Color)
        {
            case "random":
                smoke.SmokeColor.X = Random.Shared.NextSingle() * 255.0f;
                smoke.SmokeColor.Y = Random.Shared.NextSingle() * 255.0f;
                smoke.SmokeColor.Z = Random.Shared.NextSingle() * 255.0f;
                break;
            case "team":
                switch (player.TeamNum)
                {
                    case 2:
                        smoke.SmokeColor.X = 255.0f;
                        smoke.SmokeColor.Y = 0.0f;
                        smoke.SmokeColor.Z = 0.0f;
                        break;
                    case 3:
                        smoke.SmokeColor.X = 0.0f;
                        smoke.SmokeColor.Y = 0.0f;
                        smoke.SmokeColor.Z = 255.0f;
                        break;
                }
                break;
        }
    }

    private void ApplyCustomDuration(CSmokeGrenadeProjectile smoke, CCSPlayerController player)
    {
        try
        {
            float expireTime = Server.CurrentTime + Config.SmokeDuration;
            _activeSmokes[smoke.Handle] = (player, expireTime);

            AddTimer(Config.SmokeDuration, () =>
            {
                if (smoke.IsValid)
                {
                    Server.NextFrame(() =>
                    {
                        if (smoke.IsValid)
                        {
                            smoke.Remove();
                            _activeSmokes.Remove(smoke.Handle);
                        }
                    });
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error applying custom smoke duration: {ex.Message}");
        }
    }
}