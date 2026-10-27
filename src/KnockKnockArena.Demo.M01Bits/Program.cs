using KnockKnockArena.Shared.Protocol.Udp;

MovementBits keys = MovementBits.W | MovementBits.D | MovementBits.Sprint;

Console.WriteLine($"Keys held: W + D + Sprint -> movement byte {WeaponBits.ToBinaryString((byte)keys)} = 0x{(byte)keys:X2} = {(byte)keys}");
Console.WriteLine($"  [Flags] prints it as \"{keys}\"; is Sprint set? {(keys & MovementBits.Sprint) != 0}; is S set? {(keys & MovementBits.S) != 0}");

MovementBits released = keys & ~MovementBits.Sprint;
Console.WriteLine($"  release Shift (AND NOT Sprint): {WeaponBits.ToBinaryString((byte)released)} = \"{released}\"");

ButtonBits buttons = (ButtonBits)0b0000_0011;
Console.WriteLine($"  a buttons byte of 00000011 means \"{buttons}\" - both travel in the 22-byte Input packet (M06)");