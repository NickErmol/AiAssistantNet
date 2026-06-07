using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Infrastructure.Audio;

public sealed record SpeechWindow(float[] Samples, Speaker Speaker);
