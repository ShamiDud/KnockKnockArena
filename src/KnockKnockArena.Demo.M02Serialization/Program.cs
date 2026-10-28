using KnockKnockArena.Shared.Protocol;
using KnockKnockArena.Shared.Protocol.Udp;

PlayerEntityState player = new()
{
    EntityId = 1,
    X = 132f,
    Z = 132f,
    AimX = 142f,
    AimZ = 132f,
    Health = 100,
    ActiveWeapon = WeaponId.Glock,
    OwnedWeapons = WeaponBits.StartingWeapons,
    Ammo = 24,
};

byte[] payload = ProtocolSerialization.WritePayload(writer => player.WriteTo(writer));

Console.WriteLine($"Serialized: {payload.Length} bytes (1 version byte + {PlayerEntityState.SerializedSize} entity bytes)");
Console.WriteLine();
Console.WriteLine(BitConverter.ToString(payload));
Console.WriteLine();

PlayerEntityState back = ProtocolSerialization.ReadPayload(payload, PlayerEntityState.ReadFrom);
bool same = back.EntityId == player.EntityId && back.X == player.X && back.Z == player.Z &&
            back.AimX == player.AimX && back.AimZ == player.AimZ && back.Health == player.Health &&
            back.ActiveWeapon == player.ActiveWeapon && back.OwnedWeapons == player.OwnedWeapons &&
            back.Ammo == player.Ammo && back.Frags == player.Frags && back.Deaths == player.Deaths &&
            back.State == player.State && back.DashTicksLeft == player.DashTicksLeft &&
            back.DashCooldownTicks == player.DashCooldownTicks &&
            back.DashDirectionX == player.DashDirectionX && back.DashDirectionZ == player.DashDirectionZ;
Console.WriteLine($"Deserialized: x={back.X} z={back.Z} health={back.Health} weapon={back.ActiveWeapon} " +
                  $"owned={WeaponBits.ToBinaryString(back.OwnedWeapons)} ammo={back.Ammo} -> " +
                  (same ? "all fields equal" : "MISMATCH"));

byte[] stale = (byte[])payload.Clone();
stale[0] = (byte)(ProtocolConstants.Version - 1);
try
{
    ProtocolSerialization.ReadPayload(stale, PlayerEntityState.ReadFrom);
    Console.WriteLine("Version check: accepted (should not happen)");
}
catch (ProtocolException ex)
{
    Console.WriteLine($"Version byte changed to {stale[0]:X2}: rejected with \"{ex.Reason}\"");
}

Console.WriteLine();
Console.WriteLine($"WriteString(\"test\") -> {Bytes(writer => ProtocolSerialization.WriteString(writer, "test"))}");
Console.WriteLine($"WriteString(\"hæj\")  -> {Bytes(writer => ProtocolSerialization.WriteString(writer, "hæj"))}   (3 characters, 4 bytes: æ is C3 A6)");
Console.WriteLine($"BinaryWriter.Write(\"hæj\") -> {Bytes(writer => writer.Write("hæj"))} (its own 1-byte length - not our format)");

static string Bytes(Action<BinaryWriter> write)
{
    using MemoryStream stream = new();
    using (BinaryWriter writer = new(stream))
        write(writer);
    return BitConverter.ToString(stream.ToArray());
}