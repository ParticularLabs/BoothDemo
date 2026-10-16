# Booth demo

This repository contains a demo application to be run at conference booths as a showcase of Particular Platform

## Solution structure

The structure of the solution is based on the [Monitoring Demo](https://github.com/Particular/MonitoringDemo) and tries to represent a simplified async distributed e-commerce system. There are 4 main endpoints:

- ClientUI - generates `PlaceOrder` messages -- a substitute for a web front-end
- Sales - contains a saga that processess `PlaceOrder` messages and publishes `OrderPlaced` events
- Shipping - handles `OrderPlaced` and publishes `OrderShipped`. It also subscribes to `OrderBilled` but does nothing
- Billing - handles `OrderPlaced` and publishes `OrderBilled`

Because ClientUI and Sales run on RabbitMQ and Shipping and Billing -- on Azure Service Bus, there is also a messaging bridge endpoint RabbitToAzureBridge included in the solution.

The RabbitToAzureBridge process runs the `MessagingBridge` endpoint as well as a regular `NServiceBus` endpoint with a custom feature that sends the queue length metrics captured on RabbitMQ side to the metrics queue from where they are forwarded to the metrics queue on the ASB side. This workaround allows ServicePulse to display queue lenghts on both sides.

### Raffle

Most conferences expect some sort of raffle towards the end of the event to give away prize for obtaining the contaict info (scanning leads). This is supported by the Raffle endpoint that is supposed to run on a booth staff laptop.

If the `PlaceOrder` message contains the `Campaign` property set to certain value (currently `TechoramaBE2026`) then when the saga completes, it publishes `RaffleOrderCompleted` event subscribed by the Raffle endpoint. This endpoint then publishes `RaffleWinnerSelected` that **fails processing**. This is intentional. The last message contains the name of the winner in the body. In order to reveal it, the booth staff opens ServicePulse and shows the payload on screen.

## Build pipeline

Besides CI, there are two additional build workflows:

- `Images` generates docker images for Shipping, Billing and RabbitToAzureBridge
- `Release` generates Linux binaries for Sales and ClientUI

## Hardware and deployment

The system is a hybrid on-premises/cloud system with RabbitMQ used an on-premises transport and Azure Service Bus used in the cloud. The ClientUI and Sales andpoints are deployed on-premises (the booth) while Billing and Shipping are deployed to the cloud.

### Cloud resource provisioning and deployment

The `deploy.ps1` script deploys Billing and Shipping endpoints to Azure Container Apps. It also creates necessary resources (Azure Service Bus namespace). By default the endpoints scale up to 5 and down to 0 instances. It might be a good idea to modify one of them to scale down to 1 in order to speed up the processing. It is still nice to leave one of them scaling down to 0 to be able to talk about the effects of auto-scaling.

### On-premises

The booth demo hardware consists of a rack of 4 Raspberry Pi 5s and a hand-held Raspberry Pi-based console (the console  -- Clockwork Pi uConsole -- has been purchased by Szymon using his learning budget so is not included in the booth "set"). All five computers run Raspbery Pi OS (Linux) and differ slightly in software that is installed on them. There are two types of software that needs to be installed:

- debian packages -- using `sudo apt install`
- custom scripts from this repo -- copied using `scp` from staff members laptop to the Raspberry

#### Gateway

One of the Raspberry is a designated communication gateway. It runs:

- a docker compose set `TODO` that includes:
  - Messaging bridge endpoint RabbitToAzureBridge
  - PostgreSQL
  - RabbitMQ 4 (with Management Plugin and MQTT support)
- `iptables`-based packet routing between `wlan0` (conference wireless network or phone hotspot) and `eth0` (see network layout below)
- `mqtt-bridge.py` script running in a console to move messages from the queue where Button sends them to the Sales queue (it needs to add headers to make ServicePulse diagrams look good)
- `tmux-panes.sh` script running in a console that creates 3 panes and connects them via ssh to tmux sessions running on Sales Raspberries. This is the main view that shows the messages beging processed
- `button-box.py` script running in a console to intercept signals from the control button box and generate tmux commands

#### Sales

Three Raspberries running the Sales endpoint. They require the `download-sales.sh` script to be present (scp-ed) in the home folder. This script, when run, downloads the build artifacts of th latest `Release` build of the Sales endpoint and places them in the `sales` folder.

Once this is done, `sales/Sales X` starts up the endpoint where X is the instance ID (1-3).

#### Client

The handheld Raspberry Pi console runs the client endpoint. It requires `download-client.sh` and `start-client.sh` scripts to be present (scp-ed) in the home folder. This script, when run, downloads the build artifacts of th latest `Release` build of the ClientUI endpoint and places them in the `client` folder.

Once this is done, `start-client.sh` creates a tmux session and runs the client endpoint in them. The tmux session is needed when another button box (optional) is connected to the Gateway Pi to allow controlling the client.

#### Tmux architecture

tmux (terminal multiplexer) is a versatile tool for creating persistent terminal sessions on local or remote machines. In the booth demo it is used extensively to connect the inputs and outputs.

The `tmux-panes.sh` script on the Gateway creates a local tmux session `remote-grid` and, within it, creates three panes, one for each Sales Raspberry. Then, within each pane, it runs `ssh` to connect to that Pi and, when authenticated, runs tmux to create a "remote" session (on the Sales Pi, not Gateway Pi). Then the user needs to manually (_this probably could be automated_) type `sales/Sales X` to start up the endpoint.

Once this is done, you can disconnect the network cable but the process running on the Sales Pi will continue (without tmux the ssh session would die instantly). Once the cable is reconnected, the output will resume to be streamed to the parent tmux 3-pane session.

The `button-box.py` script connects to Linux input device APIs and reacts on events generated by the button box. It maps events associated with toggles, dials and buttons to sequences of keys/characters that are sent via tmux to the `remote-grid` tmux session (all panes). As a result, all three Sales endpoins are controlled from the single button box.

#### Networking

The on-premises network is connected using a MikroTik wireless router in the following way:

- MikroTik is set to switch/access point mode (not router mode)
- Gateway Pi is connected to the first port (labeled Internet)
- Sales Pis are connected to ports 2, 3 and 4
- Sales Pis have their wireless network interface `wlan0` disabled and using `eth0` on default settings
- Gateway Pi has two network interfaces, `wlan0` and `eth0`
  - `wlan0` is connected to event wifi or staff member phone hotspot
  - `eth0` is connected to MikroTik
  - `iptables` is set up to forward traffic between interfaces
  - DHCPD is set up to assign IP addresses to devices connected via MikroTik to `eth0`

#### The Button

The button is an industry-standard button box with ESP32 WROOM dev board inside. The button is connected to a GPIO port. The ESP32 runs a small piece of C code that detects the voltage change on the GPIO port and reacts by sending an MQTT message over a wifi connection (established via MikroTik).

### Software

There is a number of packages required to run the demo. Here's a list of debian packages that need to be installed via `sudo apt install`

#### Gateway

- tmux
- python3
- iptables
- iptables-persistent
- docker (`curl -fsSL https://get.docker.com -o get-docker.sh`, `sudo sh get-docker.sh`, `sudo systemctl enable docker`)
- dockge
- gh

#### Sales

- tmux
- gh

#### Client

- tmux
- gh

## Starting up

- Power up all devices
- Ensure Gateway Pi can connect to the Internet
- Open browser on the Gateway and verify:
  - PostgreSQL database is up
  - RabbitMQ broker is up
  - All containers are up in Dockge
- Open terminal window in Gateway
- In one tab run `tmux-panes.sh`
  - Log in to all panes using password `particular`
  - In each pane run `sales/Sales X` where X is 1..3
  - Verify all Sales endpoint are running
- In Clockwork Pi (Client Pi) open a terminal window
  - Run `start-client.sh`
  - Press `b` a couple of times and verify is messages are processed by Sales
- In Gateway browser verify that messages are properly bridged to Azure and not stuck in RabbitMQ
- In Gateway terminal window open a new tab
  - Run `attach-client.sh`
  - Verify if you can see the same screen as on the Clockwork Pi
- In Gateway terminal window open a new tab
  - Run `pyhton3 mqtt-bridge.py`
  - Press the button a couple of times
  - Verify that Sales endpoint is processing messages
- In Gateway terminal window open a new tab
  - Run `sudo evtest` to see which device file is associated with the controller
  - Run `python3 button-box.py <device file>` or `python3 button-box-saitek.py <device file>` to forward controller events to Sales
  - Press some buttons to verify if events are forwrded
- In Gateway terminal window open a new tab
  - Run `python3 button-box-client <device file>` or `python3 button-box-client-saitek.py <device file>` to forward controller events to Client
  - Press some buttons to verify if events are forwarded
