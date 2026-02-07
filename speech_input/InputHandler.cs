using WindowsInput;

namespace speech_input
{
    public class InputHandler
    {
        private readonly InputSimulator _simulator = new InputSimulator();

        public void TypeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            _simulator.Keyboard.TextEntry(text);
        }
    }
}
