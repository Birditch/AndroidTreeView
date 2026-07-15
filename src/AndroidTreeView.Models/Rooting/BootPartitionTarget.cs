namespace AndroidTreeView.Models.Rooting;

/// <summary>
/// Boot partition selected from device and package evidence. <see cref="Unknown"/> is never flashable.
/// </summary>
public enum BootPartitionTarget
{
    Unknown = 0,
    Boot = 1,
    InitBoot = 2
}
