namespace DosBoxxer.Core.Models;

/// <summary>
/// Per-game overrides that are merged into the base <c>dosbox.conf</c> when the game is started.
/// A <c>null</c> value means "do not override, keep whatever the base configuration says".
/// </summary>
public sealed class GameDosBoxSettings
{
    public Guid GameId { get; set; }

    /// <summary>[cpu] cycles — e.g. <c>auto</c>, <c>max</c>, <c>fixed 3000</c>.</summary>
    public string? Cycles { get; set; }

    /// <summary>[cpu] core — e.g. <c>auto</c>, <c>normal</c>, <c>dynamic</c>, <c>simple</c>.</summary>
    public string? Core { get; set; }

    /// <summary>[dosbox] machine — e.g. <c>svga_s3</c>, <c>vgaonly</c>, <c>ega</c>, <c>cga</c>, <c>tandy</c>, <c>hercules</c>.</summary>
    public string? Machine { get; set; }

    /// <summary>[dosbox] memsize in MB.</summary>
    public string? MemSize { get; set; }

    /// <summary>[render] scaler — e.g. <c>none</c>, <c>normal2x</c>, <c>normal3x</c>, <c>hq2x</c>, <c>advmame2x</c>.</summary>
    public string? Scaler { get; set; }

    /// <summary>[render] aspect — <c>true</c>/<c>false</c>.</summary>
    public bool? Aspect { get; set; }

    /// <summary>[sdl] fullscreen — <c>true</c>/<c>false</c>.</summary>
    public bool? Fullscreen { get; set; }

    /// <summary>[sdl] output — e.g. <c>surface</c>, <c>opengl</c>, <c>openglnb</c>, <c>texture</c>.</summary>
    public string? Output { get; set; }

    /// <summary>[mixer] rate.</summary>
    public string? MixerRate { get; set; }

    /// <summary>[sblaster] sbtype — e.g. <c>sb16</c>, <c>sbpro2</c>, <c>none</c>.</summary>
    public string? SoundBlasterType { get; set; }

    /// <summary>[speaker] pcspeaker — <c>true</c>/<c>false</c>.</summary>
    public bool? PcSpeaker { get; set; }

    /// <summary>
    /// Free-form additional configuration lines, applied after all structured overrides.
    /// Each line must be either a <c>[section]</c> header or a <c>key=value</c> pair.
    /// </summary>
    public string? AdditionalConfigLines { get; set; }

    /// <summary>Extra DOS commands appended to <c>[autoexec]</c> before the mount block.</summary>
    public string? PreLaunchCommands { get; set; }

    public bool HasAnyOverride =>
        !string.IsNullOrWhiteSpace(Cycles) ||
        !string.IsNullOrWhiteSpace(Core) ||
        !string.IsNullOrWhiteSpace(Machine) ||
        !string.IsNullOrWhiteSpace(MemSize) ||
        !string.IsNullOrWhiteSpace(Scaler) ||
        Aspect.HasValue ||
        Fullscreen.HasValue ||
        !string.IsNullOrWhiteSpace(Output) ||
        !string.IsNullOrWhiteSpace(MixerRate) ||
        !string.IsNullOrWhiteSpace(SoundBlasterType) ||
        PcSpeaker.HasValue ||
        !string.IsNullOrWhiteSpace(AdditionalConfigLines) ||
        !string.IsNullOrWhiteSpace(PreLaunchCommands);

    public GameDosBoxSettings Clone() => new()
    {
        GameId = GameId,
        Cycles = Cycles,
        Core = Core,
        Machine = Machine,
        MemSize = MemSize,
        Scaler = Scaler,
        Aspect = Aspect,
        Fullscreen = Fullscreen,
        Output = Output,
        MixerRate = MixerRate,
        SoundBlasterType = SoundBlasterType,
        PcSpeaker = PcSpeaker,
        AdditionalConfigLines = AdditionalConfigLines,
        PreLaunchCommands = PreLaunchCommands,
    };
}
