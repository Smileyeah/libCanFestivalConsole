#!/bin/bash

ifconfig can0 down
ip link set can0 type can bitrate 1000000
ip link set can0 type can restart-ms 100
ifconfig can0 txqueuelen 1000
ifconfig can0 up
