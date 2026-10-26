#include "network_config.h"
/* Values as stored by libnx on little-endian AArch64; verify byte order too. */
_Static_assert(LAN_BROADCAST(0x3205A8C0u, 0x00FFFFFFu) == 0xFF05A8C0u, "192.168.5.50/24 must search 192.168.5.255");
_Static_assert(LAN_BROADCAST(0x3205A8C0u, 0x0000FFFFu) == 0xFFFFA8C0u, "192.168.5.50/16 must search 192.168.255.255");
_Static_assert(LAN_BROADCAST(0x3204A8C0u, 0x00FEFFFFu) == 0xFF05A8C0u, "192.168.4.50/23 must search 192.168.5.255");
_Static_assert(LAN_BROADCAST(0x0A00000Au, 0xFCFFFFFFu) == 0x0B00000Au, "10.0.0.10/30 must search 10.0.0.11");
