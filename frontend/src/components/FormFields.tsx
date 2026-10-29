import { Checkbox, Radio, Stack, Textarea, TextInput } from '@mantine/core'
import type { Answers, FormField } from '../forms'
import type { Section } from '../sections'

type Props = { fields: FormField[]; sections: Section[]; answers: Answers; onChange: (a: Answers) => void; errors?: Record<string, string> }

// A form's own questions as recruits see them – the builder's preview and the public form (#29) share it.
export function FormFields({ fields, sections, answers, onChange, errors = {} }: Props) {
  const set = (id: string, values: string[]) => onChange({ ...answers, [id]: values })
  return (
    <Stack>
      {fields.map((f) => {
        const value = answers[f.id] ?? []
        const common = { label: f.label, required: f.required, error: errors[f.id] }
        const choices = f.type === 'Sections' ? sections.map((s) => ({ value: String(s.id), label: s.name })) : f.options.filter((o) => o.trim()).map((o) => ({ value: o, label: o }))
        switch (f.type) {
          case 'Textarea':
            return <Textarea key={f.id} {...common} autosize minRows={3} value={value[0] ?? ''} onChange={(e) => set(f.id, [e.currentTarget.value])} />
          case 'SingleChoice':
            return (
              <Radio.Group key={f.id} {...common} value={value[0] ?? null} onChange={(v) => set(f.id, [v])}>
                <Stack gap="xs" mt="xs">
                  {choices.map((c) => (
                    <Radio key={c.value} value={c.value} label={c.label} />
                  ))}
                </Stack>
              </Radio.Group>
            )
          case 'MultiChoice':
          case 'Sections':
            return (
              <Checkbox.Group key={f.id} {...common} value={value} onChange={(v) => set(f.id, v)}>
                <Stack gap="xs" mt="xs">
                  {choices.map((c) => (
                    <Checkbox key={c.value} value={c.value} label={c.label} />
                  ))}
                </Stack>
              </Checkbox.Group>
            )
          default:
            return (
              <TextInput
                key={f.id}
                {...common}
                type={f.type === 'Email' ? 'email' : f.type === 'Phone' ? 'tel' : 'text'}
                autoComplete={f.type === 'Email' ? 'email' : f.type === 'Phone' ? 'tel' : undefined}
                value={value[0] ?? ''}
                onChange={(e) => set(f.id, [e.currentTarget.value])}
              />
            )
        }
      })}
    </Stack>
  )
}
