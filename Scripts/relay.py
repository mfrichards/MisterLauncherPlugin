import serial
import time
import sys

relay_close = 'AT+CH1=1\r\n'.encode('utf-8')
relay_open = 'AT+CH1=0\r\n'.encode('utf-8')
replay_port = sys.argv[1] if len(sys.argv) > 1 else "COM3"

with serial.Serial(relay_port, baudrate=9600, bytesize=8, stopbits=1, timeout=1) as ser:
    #time.sleep(1)
    ser.write(relay_close)
    time.sleep(0.2)
    ser.write(relay_open)
