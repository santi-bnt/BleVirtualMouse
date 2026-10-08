#include <Arduino.h>
#include <HijelHID_BLEMouse.h>

namespace {

constexpr uint32_t SerialBaud = 115200;
constexpr size_t MaxCommandLength = 96;
constexpr char DeviceName[] = "BleVirtualMouse ESP32";
constexpr char Manufacturer[] = "BleVirtualMouse";

HijelBLEMouse mouse(DeviceName, Manufacturer, 100, 3, false);
String commandBuffer;
bool lastConnected = false;
bool lastPaired = false;
bool lastSubscribed = false;
uint32_t lastStatusCheck = 0;
uint32_t lastStatusReport = 0;

void sendStatus()
{
    Serial.printf(
        "STATUS connected=%d paired=%d bonded=%d subscribed=%d sent=%lu failed=%lu\n",
        mouse.isConnected() ? 1 : 0,
        mouse.isPaired() ? 1 : 0,
        mouse.isBonded() ? 1 : 0,
        mouse.isSubscribed() ? 1 : 0,
        static_cast<unsigned long>(mouse.getReportsSent()),
        static_cast<unsigned long>(mouse.getReportsFailed()));
}

bool requirePaired()
{
    if (mouse.isReady()) {
        return true;
    }

    Serial.println(mouse.isPaired() ? "ERROR NOT_SUBSCRIBED" : "ERROR NOT_PAIRED");
    return false;
}

MouseButton parseButton(const char* name, bool& valid)
{
    valid = true;
    if (strcasecmp(name, "LEFT") == 0) {
        return MouseButton::Left;
    }
    if (strcasecmp(name, "RIGHT") == 0) {
        return MouseButton::Right;
    }
    if (strcasecmp(name, "MIDDLE") == 0) {
        return MouseButton::Middle;
    }

    valid = false;
    return MouseButton::Left;
}

void processCommand(String command)
{
    command.trim();
    if (command.isEmpty()) {
        return;
    }

    if (command.equalsIgnoreCase("PING")) {
        Serial.println("PONG BleVirtualMouseESP32 protocol=1");
        return;
    }

    if (command.equalsIgnoreCase("STATUS")) {
        sendStatus();
        return;
    }

    if (command.equalsIgnoreCase("CLEAR_BONDS")) {
        mouse.clearBonds();
        Serial.println("OK CLEAR_BONDS restarting=1");
        Serial.flush();
        delay(250);
        ESP.restart();
        return;
    }

    if (command.equalsIgnoreCase("RESTART")) {
        Serial.println("OK RESTART");
        Serial.flush();
        delay(100);
        ESP.restart();
        return;
    }

    int x = 0;
    int y = 0;
    if (sscanf(command.c_str(), "MOVE %d %d", &x, &y) == 2) {
        if (!requirePaired()) {
            return;
        }

        x = constrain(x, -127, 127);
        y = constrain(y, -127, 127);
        mouse.move(static_cast<int16_t>(x), static_cast<int16_t>(y));
        Serial.printf("OK MOVE queued=1 x=%d y=%d\n", x, y);
        return;
    }

    int scroll = 0;
    if (sscanf(command.c_str(), "SCROLL %d", &scroll) == 1) {
        if (!requirePaired()) {
            return;
        }

        scroll = constrain(scroll, -127, 127);
        mouse.scroll(static_cast<int16_t>(scroll));
        Serial.printf("OK SCROLL queued=1 value=%d\n", scroll);
        return;
    }

    char buttonName[16] = {};
    if (sscanf(command.c_str(), "CLICK %15s", buttonName) == 1) {
        if (!requirePaired()) {
            return;
        }

        bool valid = false;
        MouseButton button = parseButton(buttonName, valid);
        if (!valid) {
            Serial.println("ERROR INVALID_BUTTON");
            return;
        }

        mouse.click(button, 25);
        Serial.printf("OK CLICK queued=1 button=%s\n", buttonName);
        return;
    }

    char action[12] = {};
    if (sscanf(command.c_str(), "BUTTON %15s %11s", buttonName, action) == 2) {
        if (!requirePaired()) {
            return;
        }

        bool valid = false;
        MouseButton button = parseButton(buttonName, valid);
        if (!valid) {
            Serial.println("ERROR INVALID_BUTTON");
            return;
        }

        if (strcasecmp(action, "DOWN") == 0) {
            mouse.press(button);
        }
        else if (strcasecmp(action, "UP") == 0) {
            mouse.release(button);
        }
        else {
            Serial.println("ERROR INVALID_ACTION");
            return;
        }

        Serial.printf("OK BUTTON button=%s action=%s\n", buttonName, action);
        return;
    }

    Serial.println("ERROR UNKNOWN_COMMAND");
}

void readSerialCommands()
{
    while (Serial.available() > 0) {
        char value = static_cast<char>(Serial.read());
        if (value == '\r') {
            continue;
        }

        if (value == '\n') {
            processCommand(commandBuffer);
            commandBuffer = "";
            continue;
        }

        if (commandBuffer.length() >= MaxCommandLength) {
            commandBuffer = "";
            Serial.println("ERROR COMMAND_TOO_LONG");
            continue;
        }

        commandBuffer += value;
    }
}

void reportConnectionChanges()
{
    if (millis() - lastStatusCheck < 250) {
        return;
    }

    lastStatusCheck = millis();
    bool connected = mouse.isConnected();
    bool paired = mouse.isPaired();

    if (connected != lastConnected) {
        Serial.println(connected ? "EVENT CONNECTED" : "EVENT DISCONNECTED");
        lastConnected = connected;
    }

    if (paired != lastPaired) {
        Serial.println(paired ? "EVENT PAIRED" : "EVENT UNPAIRED");
        lastPaired = paired;
    }
    bool subscribed = mouse.isSubscribed();
    if (subscribed != lastSubscribed) {
        Serial.println(subscribed ? "EVENT SUBSCRIBED" : "EVENT UNSUBSCRIBED");
        lastSubscribed = subscribed;
    }
    if (millis() - lastStatusReport >= 5000) {
        lastStatusReport = millis();
        sendStatus();
    }
}

} // namespace

void setup()
{
    Serial.begin(SerialBaud);
    commandBuffer.reserve(MaxCommandLength);
    delay(250);

    mouse.setSecurityMode(HIDSecurity::JustWorks);
    mouse.setUpdateRate(HIDRate::Hz50);
    mouse.setLogLevel(HIDLogLevel::Normal);
    mouse.begin();

    Serial.println("READY BleVirtualMouseESP32 protocol=1");
    sendStatus();
}

void loop()
{
    readSerialCommands();
    reportConnectionChanges();
    delay(1);
}
