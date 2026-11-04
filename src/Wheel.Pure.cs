namespace BetterKeyBindings
{
    // The mouse wheel as two keys, wheel up and wheel down, kept apart from Unity so tests/PureTest.cs can run it
    // headless. The caller reports the wheel's movement once per frame (positive is up).
    //   A notch is one press: its key is down for one frame and released the frame after.
    //   Notches that arrive in the same frame are queued and pressed one per frame, so a binding triggers once per notch.
    //   Fractions of a notch, as some touchpads send, add up until they make one.
    //   Turning the wheel the other way drops what was left over in the first direction.
    public sealed class Notches
    {
        public const int WheelUp = 0, WheelDown = 1;
        // Cap on notches waiting to be pressed, so one wild spin does not trigger a binding for seconds afterwards.
        public const int MaxQueued = 8;

        readonly int[] queued = new int[2];
        readonly float[] carry = new float[2];
        readonly bool[] down = new bool[2], wasDown = new bool[2];

        public void Frame(float delta)
        {
            for (int d = 0; d < 2; d++)
            {
                float amount = d == WheelUp ? delta : -delta;
                wasDown[d] = down[d];
                if (amount > 0)
                {
                    carry[d] += amount;
                    // A little slack, so ten tenths make a notch despite rounding.
                    int n = (int)(carry[d] + 0.001f);
                    carry[d] -= n;
                    if (carry[d] < 0) carry[d] = 0;
                    queued[d] = System.Math.Min(queued[d] + n, MaxQueued);
                }
                else if (amount < 0) { carry[d] = 0; queued[d] = 0; }
                down[d] = queued[d] > 0;
                if (down[d]) queued[d]--;
            }
        }

        public void Reset()
        {
            for (int d = 0; d < 2; d++) { queued[d] = 0; carry[d] = 0; down[d] = wasDown[d] = false; }
        }

        // Down this frame: a notch is pressed.
        public bool Pressed(int direction) { return down[direction]; }

        // Released this frame: the notch of the frame before. A notch pressed again in the same frame is reported as
        // both, release first, as when a key is tapped twice.
        public bool Released(int direction) { return wasDown[direction]; }
    }
}
