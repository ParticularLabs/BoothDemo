# Raspberry Pi Wi-Fi-to-Ethernet Router

Setup and recovery instructions for a Raspberry Pi acting as a NAT router, DHCP server, and optional network impairment simulator.

Reconstructed from setup and troubleshooting conversations between April and October 2026.

> **Note:** These instructions reproduce the configuration we developed. Some later changes were discussed but not verified on the running device. See [Known uncertainties](#known-uncertainties).

## 1. Network architecture

```text
       Internet / Wi-Fi
              |
            wlan0
              |
    +--------------------+
    | Raspberry Pi Router|
    |                    |
    | DHCP: dnsmasq      |
    | NAT: Linux         |
    | LAN: 192.168.10.1  |
    +--------------------+
              |
            eth0
              |
       MikroTik ether1
              |
      MikroTik bridge
         /    |    \
       Pi 2  Pi 3  Pi 4
```

| Component | Role | Configuration |
|---|---|---|
| Router `wlan0` | Internet uplink | Wi-Fi client, DHCP |
| Router `eth0` | Local LAN | `192.168.10.1/24` |
| dnsmasq | DHCP and DNS | `192.168.10.50–150` |
| Kernel | Routing | IPv4 forwarding |
| Firewall | NAT | Masquerade through `wlan0` |
| MikroTik | Ethernet switch | Bridge ports, disable DHCP/NAT |
| Client Pis | Network clients | DHCP |

The router Pi is responsible for routing. The MikroTik is used only for Ethernet switching.

## 2. Required packages

### Core packages

```bash
sudo apt update

sudo apt install -y \
  dnsmasq \
  iproute2 \
  iptables \
  tcpdump
```

| Package | Purpose |
|---|---|
| `dnsmasq` | DHCP server and DNS forwarder |
| `network-manager` | Connection management; normally preinstalled |
| `iproute2` | `ip`, `tc`, routing diagnostics |
| `iptables` | Firewall and NAT configuration |
| `tcpdump` | Network packet diagnostics |

### Optional packages

```bash
sudo apt install -y \
  isc-dhcp-client \
  nftables
```

- `isc-dhcp-client` provides `dhclient`.
- `nftables` supports the original firewall approach.
- `iptables-persistent` can save iptables rules, but requires care when Docker is installed.

**Important:** The original router initially used nftables because `iptables` was unavailable. Later troubleshooting used iptables alongside Docker. Inspect the current firewall before modifying it.

## 3. Configure the Wi-Fi uplink

Check the interfaces:

```bash
nmcli device status
nmcli connection show --active
```

List available Wi-Fi networks:

```bash
nmcli device wifi list
```

Connect if necessary:

```bash
sudo nmcli device wifi connect "SSID" password "PASSWORD"
```

Check the assigned address:

```bash
ip -4 addr show wlan0
```

Verify Internet connectivity:

```bash
ping -c 3 1.1.1.1
getent hosts example.com
```

The default route should use `wlan0`.

Check:

```bash
ip route
```

## 4. Configure the LAN interface

The Ethernet interface must have a static address.

The original configuration uses:

```text
192.168.10.1/24
```

Raspberry Pi OS uses NetworkManager. Older instructions using `dhcpcd` may not work.

List connections:

```bash
nmcli connection show
```

Assuming the connection is named `Wired connection 1`:

```bash
sudo nmcli connection modify \
  "Wired connection 1" \
  ipv4.method manual \
  ipv4.addresses 192.168.10.1/24 \
  ipv4.never-default yes
```

Ensure this Ethernet connection does not specify an unwanted IPv4 gateway.

Apply:

```bash
sudo nmcli connection up "Wired connection 1"
```

Verify:

```bash
ip -4 addr show eth0
ip route
```

Expected:

```text
eth0: 192.168.10.1/24
```

**Warning:** Activating this connection may interrupt SSH sessions over Ethernet.

## 5. Enable IPv4 forwarding

Create:

```text
/etc/sysctl.d/99-router.conf
```

Contents:

```ini
net.ipv4.ip_forward=1
```

Apply:

```bash
sudo sysctl --system
```

Verify:

```bash
cat /proc/sys/net/ipv4/ip_forward
```

Expected output:

```text
1
```

This permits kernel-level routing between interfaces. Firewall rules must also allow forwarded traffic.

## 6. Configure the DHCP server

Install dnsmasq if necessary:

```bash
sudo apt install dnsmasq
```

Create:

```text
/etc/dnsmasq.d/pi-router.conf
```

Contents:

```ini
interface=eth0
bind-interfaces

dhcp-range=192.168.10.50,192.168.10.150,255.255.255.0,24h

dhcp-option=option:router,192.168.10.1
dhcp-option=option:dns-server,192.168.10.1
```

The DHCP settings provide:

| Setting | Value |
|---|---|
| Address range | `192.168.10.50–150` |
| Subnet | `255.255.255.0` |
| Gateway | `192.168.10.1` |
| DNS | `192.168.10.1` |
| Lease duration | 24 hours |

The explicit DHCP options are recommended for a reproducible setup. Their presence in the original saved configuration was not verified.

Check syntax:

```bash
sudo dnsmasq --test
```

Enable the service:

```bash
sudo systemctl enable --now dnsmasq
```

Restart after changes:

```bash
sudo systemctl restart dnsmasq
```

Verify:

```bash
systemctl status dnsmasq
```

If the configuration is not being applied, verify that the installed dnsmasq service loads files from `/etc/dnsmasq.d/`.

### Manual DHCP diagnostic

During the original setup, starting dnsmasq manually successfully served DHCP addresses:

```bash
sudo dnsmasq -d -i eth0 \
  --dhcp-range=192.168.10.50,192.168.10.150,12h
```

Use this only for troubleshooting, after stopping the existing dnsmasq service or instance.

Manual execution does not provide startup persistence.

## 7. Configure the MikroTik switch

The MikroTik should operate as a transparent Ethernet bridge.

Using RouterOS/WinBox:

1. Open **Bridge**.
2. Create `bridge1`, if necessary.
3. Add `ether1` and all client Ethernet ports to the same bridge.
4. Disable the MikroTik DHCP server.
5. Disable the MikroTik NAT masquerade rules.
6. Disable any obsolete DHCP client bound to `ether1`.
7. Optionally configure a management address on `bridge1`.

Suggested management address:

```text
192.168.10.2/24
```

The Raspberry Pi must be connected to a bridged Ethernet port.

**Warning:** Changing bridge membership or management addresses can disconnect remote access to the MikroTik.

## 8. Configure NAT and forwarding

### Important firewall distinction

There are two approaches from our setup history:

1. Original nftables-based configuration.
2. Later iptables rules, including Docker-specific forwarding.

Do not apply both indiscriminately.

Before making changes:

```bash
sudo nft list ruleset
sudo iptables -S FORWARD
sudo iptables -t nat -S POSTROUTING
```

Also determine whether Docker is installed and which firewall backend it uses.

### 8.1 iptables forwarding rules

For a system using the iptables forwarding chain:

```bash
sudo iptables -I FORWARD 1 \
  -i eth0 -o wlan0 \
  -s 192.168.10.0/24 \
  -j ACCEPT
```

Allow established return traffic:

```bash
sudo iptables -I FORWARD 2 \
  -i wlan0 -o eth0 \
  -d 192.168.10.0/24 \
  -m conntrack \
  --ctstate RELATED,ESTABLISHED \
  -j ACCEPT
```

Enable NAT:

```bash
sudo iptables -t nat -A POSTROUTING \
  -s 192.168.10.0/24 \
  -o wlan0 \
  -j MASQUERADE
```

Check for an existing equivalent masquerade rule before adding another.

### 8.2 Docker-specific forwarding rules

Docker may install its own forwarding chains and policies.

When Docker uses its iptables backend, use `DOCKER-USER` for rules that should be evaluated before Docker's forwarding restrictions.

Inspect:

```bash
sudo iptables -S DOCKER-USER
```

Allow outgoing forwarded traffic:

```bash
sudo iptables -I DOCKER-USER 1 \
  -i eth0 -o wlan0 \
  -s 192.168.10.0/24 \
  -j ACCEPT
```

Allow return traffic:

```bash
sudo iptables -I DOCKER-USER 2 \
  -i wlan0 -o eth0 \
  -d 192.168.10.0/24 \
  -m conntrack \
  --ctstate RELATED,ESTABLISHED \
  -j ACCEPT
```

Keep the appropriate masquerade rule in the NAT postrouting chain.

These Docker-specific commands are recommendations reconstructed from the May troubleshooting, not a verified snapshot of the final running rules.

They apply to Docker's iptables backend, not automatically to its native nftables backend.

### 8.3 Original nftables approach

The original alternative was an IPv4 NAT table with a postrouting masquerade rule.

Example:

```nft
table ip pi_router_nat {
    chain postrouting {
        type nat hook postrouting priority srcnat;
        policy accept;

        ip saddr 192.168.10.0/24 oifname "wlan0" masquerade
    }
}
```

This NAT configuration alone is insufficient if the existing filter rules deny forwarding.

Preserve existing nftables tables and Docker-managed firewall rules.

**Do not run a blanket `flush ruleset` on a running Docker host.**

## 9. Firewall persistence

The original attempt:

```bash
sudo netfilter-persistent save
```

failed because the command was unavailable.

The suggested installation was:

```bash
sudo apt install iptables-persistent
```

This provides `netfilter-persistent`.

However, saving a complete live firewall ruleset can capture Docker-generated chains.

For a Docker host, prefer a dedicated boot-time service or other managed mechanism that applies only router-owned rules after the relevant firewall backend is initialized.

The exact persistence mechanism currently installed on the router was not confirmed.

## 10. Configure client Raspberry Pis

Client Ethernet interfaces should obtain their addresses using DHCP.

Check:

```bash
nmcli device status
ip -4 addr show eth0
ip route
```

Reconnect:

```bash
sudo nmcli device connect eth0
```

If `dhclient` is installed, manually renew:

```bash
sudo dhclient -v -r eth0
sudo dhclient -v eth0
```

Expected client configuration:

```text
IP:       192.168.10.50–150
Netmask:  255.255.255.0
Gateway:  192.168.10.1
DNS:      192.168.10.1
```

## 11. Verification checklist

### Router

```bash
nmcli device status
ip -br addr
ip route
```

```bash
cat /proc/sys/net/ipv4/ip_forward
```

```bash
systemctl status dnsmasq
```

```bash
ping -c 3 1.1.1.1
getent hosts example.com
```

### Client

Check DHCP address:

```bash
ip -4 addr show eth0
```

Check gateway:

```bash
ip route
```

Ping router:

```bash
ping -c 3 192.168.10.1
```

Ping Internet IP:

```bash
ping -c 3 1.1.1.1
```

Test DNS:

```bash
getent hosts example.com
```

Interpret results:

| Observation | Likely area to investigate |
|---|---|
| No DHCP address | dnsmasq, Ethernet link, client DHCP |
| Router reachable, Internet IP unreachable | Routing, NAT, forwarding rules |
| Internet IP reachable, DNS fails | DNS settings |
| Router has no Internet access | Wi-Fi uplink/default route |
| Routing breaks after Docker starts | Docker forwarding/firewall policies |
| Routing breaks after reboot | Configuration persistence |

## 12. DHCP troubleshooting

On the router:

```bash
sudo journalctl -u dnsmasq -f
```

Capture DHCP messages:

```bash
sudo tcpdump -ni eth0 \
  'udp port 67 or udp port 68'
```

Expected sequence:

```text
DHCPDISCOVER
DHCPOFFER
DHCPREQUEST
DHCPACK
```

Interpretation:

- No DISCOVER: investigate the client or physical link.
- DISCOVER but no OFFER: investigate dnsmasq and server firewall.
- Complete exchange but no configured IP: investigate client configuration.

## 13. Network impairment simulation (optional)

The router can simulate poor network conditions using `tc netem`.

`tc` is provided by `iproute2`.

### Delay traffic toward LAN clients

```bash
sudo tc qdisc add dev eth0 root netem delay 100ms
```

### Delay traffic toward the Internet

```bash
sudo tc qdisc add dev wlan0 root netem delay 100ms
```

### Simulate jitter and packet loss

```bash
sudo tc qdisc replace dev eth0 root \
  netem delay 100ms 20ms loss 1%
```

### Simulate 10% packet loss

```bash
sudo tc qdisc replace dev eth0 root \
  netem loss 10%
```

### Inspect configuration

```bash
tc qdisc show dev eth0
tc qdisc show dev wlan0
```

### Remove impairment

```bash
sudo tc qdisc del dev eth0 root
sudo tc qdisc del dev wlan0 root
```

If necessary:

```bash
sudo modprobe sch_netem
```

### Important behavior

`tc netem` modifies outgoing traffic on the selected interface.

For the router:

- `wlan0` handles outgoing traffic toward the upstream network.
- `eth0` handles outgoing traffic toward wired clients.

A local ping generated by the router may not demonstrate an impairment applied to `eth0`.

Test from a client behind the router.

We confirmed in April that traffic impairment worked when tested from other Raspberry Pis.

The configuration is normally temporary unless explicitly restored on boot.

## 14. Configuration inventory

These files or settings define the router:

| Path or component | Purpose |
|---|---|
| NetworkManager Ethernet profile | Static `eth0` address |
| NetworkManager Wi-Fi profile | Upstream Wi-Fi |
| `/etc/sysctl.d/99-router.conf` | IPv4 forwarding |
| `/etc/dnsmasq.d/pi-router.conf` | DHCP/DNS |
| nftables or iptables configuration | NAT and forwarding |
| Optional firewall startup service | Firewall persistence |
| Optional `tc` startup service | Network impairments |
| MikroTik bridge configuration | Ethernet switching |

To inspect the current router:

```bash
cat /etc/os-release
nmcli connection show
nmcli device status
ip -br addr
ip route

cat /proc/sys/net/ipv4/ip_forward
sudo sysctl --system

systemctl is-enabled dnsmasq
systemctl status dnsmasq
sudo dnsmasq --test

sudo nft list ruleset
sudo iptables-save

sudo docker info

tc qdisc show dev eth0
tc qdisc show dev wlan0
```

Some commands may be unavailable if their corresponding packages are not installed.

## 15. Original troubleshooting history

| Date | Issue | Result |
|---|---|---|
| 2026-04-06 | Configuring Wi-Fi-to-Ethernet router | Selected Pi router + MikroTik switch |
| 2026-04-06 | `dhcpcd.service` missing | Used NetworkManager |
| 2026-04-06 | Empty `/etc/sysctl.conf` | Used sysctl.d |
| 2026-04-06 | `iptables` unavailable | Discussed nftables |
| 2026-04-06 | Client DHCP failed | Found dnsmasq missing |
| 2026-04-06 | DHCP service issues | Manual dnsmasq worked |
| 2026-04-06 | Client renewed DHCP | Confirmed working |
| 2026-04-06 | Network delay testing | `tc netem` worked |
| 2026-05-04 | DHCP and DNS worked; Internet failed | Docker/NAT rules proposed |
| 2026-05-05 | `netfilter-persistent` missing | `iptables-persistent` suggested |
| 2026-10-16 | Client Pi failed DHCP | Troubleshooting discussed; resolution not confirmed |

## 16. Known uncertainties

The original setup successfully provided DHCP, and later conversations continued referring to the Raspberry Pi as a router.

However, the following are not verified against the currently running device:

- Current Ethernet and Wi-Fi addresses.
- Active firewall backend.
- Complete NAT/filter ruleset.
- Firewall persistence after reboot.
- dnsmasq service persistence.
- Behavior after Docker restarts.
- Whether `tc netem` is currently active.
- Whether the October DHCP issue was resolved.

A fully reproducible backup should be finalized after comparing these instructions with the live configuration.

## References

- [Raspberry Pi OS networking](https://www.raspberrypi.com/documentation/computers/configuration.html)
- [dnsmasq manual](https://thekelleys.org.uk/dnsmasq/docs/dnsmasq-man.html)
- [Docker iptables firewall](https://docs.docker.com/engine/network/firewall-iptables/)
- [Docker nftables firewall](https://docs.docker.com/engine/network/firewall-nftables/)
