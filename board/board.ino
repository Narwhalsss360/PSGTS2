#define MAX_SKETCH_BINDINGS (16)
#include <AutoBind.h>
#include <NTimer.h>
#include <NPush.h>
#include <NFuncs.h>

using delay_t = unsigned long;

constexpr const delay_t LOOP_DELAY = 10;

using baudrate_t = unsigned long;

constexpr const baudrate_t BAUDRATE = 1000000;

enum DIO : byte {
  DOWN,
  UP,
  GREEN,
  RED,
  YELLOW,
  BLUE,
  ORANGE,
  SELECT,
  START,
  LEFT,
  RIGHT,
  TILT,
  HOME
};

constexpr const byte DIO_PINS[] = {
  2,
  3,
  4,
  5,
  6,
  7,
  8,
  9,
  10,
  11,
  12,
  A3,
  A2
};

enum AIO : byte {
  WHAMMY_BAR,
  PICKUP_SELECTOR
};

constexpr const int AIO_RANGE[][2] = {
  { 0, 840 },
  { 150, 1023 }
};

constexpr const byte AIO_PINS[] = {
  A7,
  A6
};

constexpr const ntime_t DEBOUNCE = 5_ms;

Push dio[] = {
  Push(DIO_PINS[DIO::DOWN], true, DEBOUNCE),
  Push(DIO_PINS[DIO::UP], true, DEBOUNCE),
  Push(DIO_PINS[DIO::GREEN], true, DEBOUNCE),
  Push(DIO_PINS[DIO::RED], true, DEBOUNCE),
  Push(DIO_PINS[DIO::YELLOW], true, DEBOUNCE),
  Push(DIO_PINS[DIO::BLUE], true, DEBOUNCE),
  Push(DIO_PINS[DIO::ORANGE], true, DEBOUNCE),
  Push(DIO_PINS[DIO::SELECT], true, DEBOUNCE),
  Push(DIO_PINS[DIO::START], true, DEBOUNCE),
  Push(DIO_PINS[DIO::LEFT], true, DEBOUNCE),
  Push(DIO_PINS[DIO::RIGHT], true, DEBOUNCE),
  Push(DIO_PINS[DIO::TILT], true, DEBOUNCE),
  Push(DIO_PINS[DIO::HOME], true, DEBOUNCE)
};

constexpr const int AIO_COUNT = sizeof(AIO_PINS) / sizeof(AIO_PINS[0]);

constexpr const int DIO_COUNT = sizeof(dio) / sizeof(dio[0]);

#pragma pack(push, 1)
struct {
  uint32_t magic = 0xABC0FFEE;
  ntime_t::int_type uptime;
  bool dio[DIO_COUNT];
  float aio[AIO_COUNT];
} state;
#pragma pack(pop)

void setup() {
  Serial.begin(BAUDRATE);
  for (int i = 0; i < DIO_COUNT; i++) {
    dio[i].push += [](PushedEventArgs& args) {
      state.dio[args.sender - dio] = true;
    };

    dio[i].release += [](ReleasedEventArgs& args) {
      state.dio[args.sender - dio] = false;
    };
  }
}

void loop() {
  for (int i = 0; i < AIO_COUNT; i++) {
    float& value = state.aio[i];
    value = mapValue<float>(analogRead(AIO_PINS[i]), AIO_RANGE[i][LOW], AIO_RANGE[i][HIGH], 0, 1);
    state.aio[i] = constrain(value, 0, 1);
  }
  state.uptime = uptime();
  Serial.write(reinterpret_cast<uint8_t*>(&state), sizeof(state));
  Serial.flush();
  delay(LOOP_DELAY);
}
