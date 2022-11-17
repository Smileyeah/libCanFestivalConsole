#!/bin/bash

ifconfig $1 down
ip link set $1 type can bitrate 1000000
ip link set $1 type can restart-ms 100
ifconfig $1 txqueuelen 1000
ifconfig $1 up
