#ifndef NETWORK_CONFIG_H
#define NETWORK_CONFIG_H
#include <stdint.h>
/* Bitwise operations preserve network byte order. Do not assume a /24 mask. */
#define LAN_BROADCAST(address, mask) ((uint32_t)(address) | ~(uint32_t)(mask))
#endif
