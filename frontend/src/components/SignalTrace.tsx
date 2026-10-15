// A brain-signal-like trace ending in the check-mark spike of the logo. Draws itself once (theme.css → .trace).
export function SignalTrace({ color = 'var(--mantine-color-signal-4)', height = 56 }: { color?: string; height?: number }) {
  return (
    <svg className="trace" width="100%" height={height} viewBox="0 0 600 60" preserveAspectRatio="none" aria-hidden="true" style={{ display: 'block' }}>
      <path
        d="M0 40H110l7-6 7 11 7-9 6 4H290l5-9 7 15 6-8H440l9 11 20-38 9 25H600"
        pathLength={1}
        fill="none"
        stroke={color}
        strokeWidth="2.5"
        strokeLinecap="round"
        strokeLinejoin="round"
        vectorEffect="non-scaling-stroke"
      />
    </svg>
  )
}
