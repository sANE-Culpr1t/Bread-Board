// Simulated device lists. The native app (Prompt 6) replaces these with WASAPI enumeration.
export const INPUT_DEVICES = [
  'Microphone (Realtek High Definition Audio)',
  'Headset Microphone (USB Audio)',
  'CABLE Output (VB-Audio Virtual Cable)',
];

export const OUTPUT_DEVICES = [
  'CABLE Input (VB-Audio Virtual Cable)',
  'Speakers (Realtek High Definition Audio)',
  'Headphones (USB Audio)',
];

export const MONITOR_DEVICES = [
  'Headphones (USB Audio)',
  'Speakers (Realtek High Definition Audio)',
];

export const isVirtualOutput = (name: string) => /CABLE Input|VB-Audio/i.test(name) && !/Output/i.test(name);

/** Recording from the cable's output while also sending into its input loops audio back on itself. */
export const isFeedbackLoop = (input: string, output: string) =>
  /CABLE Output/i.test(input) && /CABLE Input/i.test(output);
