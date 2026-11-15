import { AppWindow } from 'lucide-react';
import { useApp } from '../state/AppStore';
import { RUNNING_APPS } from '../lib/devices';
import { cx } from '../lib/util';

/** Choose which running app's audio is merged with the mic. (Simulated window list.) */
export function AppSourcePicker({ className }: { className?: string }) {
  const { data, setData } = useApp();
  const src = data.devices.appSource;
  const value = src ? src.processName : '';
  return (
    <label className={cx('flex min-w-0 items-center gap-2 rounded-full bg-side pl-3 text-sm', className)} title="App whose audio is captured and mixed into the virtual mic">
      <AppWindow size={15} className="shrink-0 text-amber" />
      <select
        aria-label="App audio source"
        value={value}
        onChange={(e) => {
          const app = RUNNING_APPS.find((a) => a.processName === e.target.value) ?? null;
          setData((d) => ({ ...d, devices: { ...d.devices, appSource: app } }));
        }}
        className="h-9 min-w-0 flex-1 cursor-pointer appearance-none truncate bg-transparent pr-3 outline-none"
      >
        <option value="">No app selected</option>
        {RUNNING_APPS.map((a) => (
          <option key={a.processName} value={a.processName}>{a.title} ({a.processName})</option>
        ))}
      </select>
    </label>
  );
}
