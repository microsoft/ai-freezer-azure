// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

#include <WiFi.h>
#include "Esp32MQTTClient.h"
#include <TimeLib.h>

#include "temp_sensor.h"
#include "config.h"

// #define INTERVAL 300000
#define INTERVAL 60000
#define DEVICE_ID "Esp32Device"
#define MESSAGE_MAX_LEN 256

const char *messageData = "{\"deviceId\":\"%s\", \"messageId\":%d, \"Temperature\":%f}";

int messageCount = 1;
static bool hasWifi = false;
static bool messageSending = true;
static uint64_t send_interval_ms;

// Time variables
static const char ntpServerName[] = "us.pool.ntp.org";
const int timeZone = -7;  // Pacific Daylight Time (USA)

WiFiUDP Udp;
unsigned int localPort = 8888;  // local port to listen for UDP packets

time_t getNtpTime();
void digitalClockDisplay();
void printDigits(int digits);
void sendNTPpacket(IPAddress &address);
// end time variables

temp_sensor tmp;

// Utilities
static void InitWifi();

void setup()
{
    Serial.begin(115200);
    Serial.println("ESP32 Device");
    Serial.println("Initializing...");

    // Initialize the WiFi module
    Serial.println(" > WiFi");
    hasWifi = false;
    InitWifi();
    if (!hasWifi)
    {
        return;
    }

    Serial.println(" > IoT Hub");

    Esp32MQTTClient_Init((const uint8_t*)connectionString, true);

    Udp.begin(localPort);
    Serial.println("waiting for NTP time sync");
    setSyncProvider(getNtpTime);
    setSyncInterval(300);
    
    tmp.init();

    send_interval_ms = millis();
    while(second() != 0);
}

void loop()
{
    if (hasWifi)
    {
        if (messageSending && 
            (int)(millis() - send_interval_ms) >= INTERVAL && second() <= 50)
        {
            // Create a variable to hold the message payload
            char messagePayload[MESSAGE_MAX_LEN];
            // Read temperature sensor
            float temperature =  tmp.temperature();

            // Build message string to send to IoT Hub
            snprintf(messagePayload, MESSAGE_MAX_LEN, messageData, DEVICE_ID, messageCount++, temperature);
            Serial.println(messagePayload);

            // Create event from string
            EVENT_INSTANCE* message = Esp32MQTTClient_Event_Generate(messagePayload, MESSAGE);

            // Send telemetry to IoT Hub
            Esp32MQTTClient_SendEventInstance(message);
            
            send_interval_ms = millis();
        }
        else
        {
           Esp32MQTTClient_Check();
        }
      }
    delay(10);
}

/*-------- NTP code ----------*/
// The code below is presumably licensed under LGPL and has been copied from:
// https://github.com/PaulStoffregen/Time/tree/master/examples/TimeNTP_ESP8266WiFi

const int NTP_PACKET_SIZE = 48; // NTP time is in the first 48 bytes of message
byte packetBuffer[NTP_PACKET_SIZE]; //buffer to hold incoming & outgoing packets

time_t getNtpTime()
{
    IPAddress ntpServerIP; // NTP server's ip address

    while (Udp.parsePacket() > 0) ; // discard any previously received packets
    Serial.println("Transmit NTP Request");
    // get a random server from the pool
    WiFi.hostByName(ntpServerName, ntpServerIP);
    Serial.print(ntpServerName);
    Serial.print(": ");
    Serial.println(ntpServerIP);
    sendNTPpacket(ntpServerIP);
    uint32_t beginWait = millis();
    while (millis() - beginWait < 1500) {
      int size = Udp.parsePacket();
      if (size >= NTP_PACKET_SIZE) {
        Serial.println("Receive NTP Response");
        Udp.read(packetBuffer, NTP_PACKET_SIZE);  // read packet into the buffer
        unsigned long secsSince1900;
        // convert four bytes starting at location 40 to a long integer
        secsSince1900 =  (unsigned long)packetBuffer[40] << 24;
        secsSince1900 |= (unsigned long)packetBuffer[41] << 16;
        secsSince1900 |= (unsigned long)packetBuffer[42] << 8;
        secsSince1900 |= (unsigned long)packetBuffer[43];
        return secsSince1900 - 2208988800UL + timeZone * SECS_PER_HOUR;
      }
    }
    Serial.println("No NTP Response :-(");
    return 0; // return 0 if unable to get the time
}

// send an NTP request to the time server at the given address
void sendNTPpacket(IPAddress &address)
{
    // set all bytes in the buffer to 0
    memset(packetBuffer, 0, NTP_PACKET_SIZE);
    // Initialize values needed to form NTP request
    // (see URL above for details on the packets)
    packetBuffer[0] = 0b11100011;   // LI, Version, Mode
    packetBuffer[1] = 0;     // Stratum, or type of clock
    packetBuffer[2] = 6;     // Polling Interval
    packetBuffer[3] = 0xEC;  // Peer Clock Precision
    // 8 bytes of zero for Root Delay & Root Dispersion
    packetBuffer[12] = 49;
    packetBuffer[13] = 0x4E;
    packetBuffer[14] = 49;
    packetBuffer[15] = 52;
    // all NTP fields have been given values, now
    // you can send a packet requesting a timestamp:
    Udp.beginPacket(address, 123); //NTP requests are to port 123
    Udp.write(packetBuffer, NTP_PACKET_SIZE);
    Udp.endPacket();
}

static void InitWifi()
{
    Serial.println("Connecting to WiFi...");
    WiFi.begin(ssid, password);
    while (WiFi.status() != WL_CONNECTED) {
        delay(500);
        Serial.print(".");
    }
    hasWifi = true;
    Serial.println("WiFi connected");
}