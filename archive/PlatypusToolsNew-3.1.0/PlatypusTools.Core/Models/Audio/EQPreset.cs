using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Audio;

/// <summary>
/// 10-band equalizer preset with common presets built-in.
/// </summary>
public class EQPreset
{
    public string Name { get; set; } = "Custom";
    public double Preamp { get; set; } = 0;
    
    // 10-band EQ (in dB, range -12 to +12)
    public double Band32Hz { get; set; } = 0;   // Sub-bass
    public double Band64Hz { get; set; } = 0;   // Bass
    public double Band125Hz { get; set; } = 0;  // Low-mid bass
    public double Band250Hz { get; set; } = 0;  // Mid-bass
    public double Band500Hz { get; set; } = 0;  // Low-mid
    public double Band1kHz { get; set; } = 0;   // Mid
    public double Band2kHz { get; set; } = 0;   // Upper-mid
    public double Band4kHz { get; set; } = 0;   // Presence
    public double Band8kHz { get; set; } = 0;   // Brilliance
    public double Band16kHz { get; set; } = 0;  // Air
    
    public double[] GetBands() => new[]
    {
        Band32Hz, Band64Hz, Band125Hz, Band250Hz, Band500Hz,
        Band1kHz, Band2kHz, Band4kHz, Band8kHz, Band16kHz
    };
    
    public void SetBands(double[] bands)
    {
        if (bands.Length >= 10)
        {
            Band32Hz = bands[0];
            Band64Hz = bands[1];
            Band125Hz = bands[2];
            Band250Hz = bands[3];
            Band500Hz = bands[4];
            Band1kHz = bands[5];
            Band2kHz = bands[6];
            Band4kHz = bands[7];
            Band8kHz = bands[8];
            Band16kHz = bands[9];
        }
    }
    
    // Built-in presets
    public static EQPreset Flat => new() { Name = "Flat" };
    
    public static EQPreset Rock => new()
    {
        Name = "Rock",
        Band32Hz = 4, Band64Hz = 3, Band125Hz = 2, Band250Hz = 0, Band500Hz = -1,
        Band1kHz = 0, Band2kHz = 2, Band4kHz = 3, Band8kHz = 4, Band16kHz = 4
    };
    
    public static EQPreset Pop => new()
    {
        Name = "Pop",
        Band32Hz = -1, Band64Hz = 2, Band125Hz = 4, Band250Hz = 4, Band500Hz = 2,
        Band1kHz = 0, Band2kHz = -1, Band4kHz = -1, Band8kHz = 2, Band16kHz = 2
    };
    
    public static EQPreset Jazz => new()
    {
        Name = "Jazz",
        Band32Hz = 3, Band64Hz = 2, Band125Hz = 1, Band250Hz = 2, Band500Hz = -1,
        Band1kHz = -1, Band2kHz = 0, Band4kHz = 1, Band8kHz = 2, Band16kHz = 3
    };
    
    public static EQPreset Classical => new()
    {
        Name = "Classical",
        Band32Hz = 4, Band64Hz = 3, Band125Hz = 2, Band250Hz = 1, Band500Hz = -1,
        Band1kHz = -1, Band2kHz = 0, Band4kHz = 2, Band8kHz = 3, Band16kHz = 3
    };
    
    public static EQPreset Electronic => new()
    {
        Name = "Electronic",
        Band32Hz = 4, Band64Hz = 3, Band125Hz = 1, Band250Hz = 0, Band500Hz = -2,
        Band1kHz = 2, Band2kHz = 0, Band4kHz = 1, Band8kHz = 3, Band16kHz = 4
    };
    
    public static EQPreset BassBoost => new()
    {
        Name = "Bass Boost",
        Band32Hz = 6, Band64Hz = 5, Band125Hz = 4, Band250Hz = 2, Band500Hz = 0,
        Band1kHz = 0, Band2kHz = 0, Band4kHz = 0, Band8kHz = 0, Band16kHz = 0
    };
    
    public static EQPreset VocalBoost => new()
    {
        Name = "Vocal Boost",
        Band32Hz = -2, Band64Hz = -1, Band125Hz = 0, Band250Hz = 2, Band500Hz = 4,
        Band1kHz = 4, Band2kHz = 3, Band4kHz = 2, Band8kHz = 0, Band16kHz = -1
    };
    
    public static List<EQPreset> AllPresets => new()
    {
        Flat, Rock, Pop, Jazz, Classical, Electronic, BassBoost, VocalBoost
    };
}
