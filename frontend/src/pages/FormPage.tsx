import { Alert, Anchor, Button, Checkbox, Collapse, Container, Paper, Skeleton, Stack, Text, TextInput, ThemeIcon } from '@mantine/core'
import { IconCheck } from '@tabler/icons-react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router'
import { api, ApiError, fieldError } from '../api'
import { FormFields } from '../components/FormFields'
import { PublicHeader } from '../components/PublicPage'
import type { Answers, FormField } from '../forms'
import type { Dictionary } from '../locales'
import { usePublicLanguage } from '../publicLanguage'

type PublicForm = {
  clubName: string | null
  contactEmail: string | null
  title: string
  description: string | null
  fields: FormField[]
  sections: { id: number; name: string }[]
}

const formTitle = (t: Dictionary) => t.formPageTitle

// Public recruitment form – no account; anyone with the link applies while the form is open.
export function FormPage() {
  const { id } = useParams()
  const base = `/api/public/forms/${id}`
  const form = useQuery({ queryKey: ['public-form', id], queryFn: () => api<PublicForm>(base), retry: false })
  const { language, t, choose } = usePublicLanguage(undefined, formTitle)

  return (
    <>
      <PublicHeader club={form.data?.clubName} title={form.data?.title ?? t.formPageTitle} t={t} language={language} onLanguage={choose}>
        {form.data?.description && (
          <Text c="cortex.0" mt="md" maw={560} style={{ whiteSpace: 'pre-line' }}>
            {form.data.description}
          </Text>
        )}
      </PublicHeader>

      <Container size="sm" py="xl" px="md">
        {form.isPending && <Skeleton height={300} />}
        {form.isError && <Alert color="red" title={form.error instanceof ApiError && form.error.status === 410 ? t.formClosed : t.formNotFound} />}
        {form.data && <Application form={form.data} base={base} t={t} language={language} />}
      </Container>
    </>
  )
}

function Application({ form, base, t, language }: { form: PublicForm; base: string; t: Dictionary; language: string }) {
  const [person, setPerson] = useState({ fullName: '', email: '', gdprConsent: false })
  const [answers, setAnswers] = useState<Answers>({})
  const [missing, setMissing] = useState<string[]>([])
  const [privacyOpen, setPrivacyOpen] = useState(false)

  const submit = useMutation({
    mutationFn: () => api(`${base}/responses`, { method: 'POST', json: { ...person, language, answers } }),
    onSuccess: () => window.scrollTo({ top: 0 }),
  })

  if (submit.isSuccess)
    return (
      <Paper withBorder p="lg" shadow="sm">
        <Stack align="flex-start">
          <ThemeIcon color="signal" size={44} radius="xl">
            <IconCheck size={26} />
          </ThemeIcon>
          <Text fw={600} ff="heading" fz="lg">
            {t.submitted}
          </Text>
          <Text>{t.submittedNote}</Text>
          <Button variant="default" onClick={() => submit.reset()}>
            {t.editAnswers}
          </Button>
        </Stack>
      </Paper>
    )

  // Server messages are English – show our own translated ones instead.
  const invalid = (field: string, message: string) => (fieldError(submit.error, field) ? message : undefined)
  const errors = Object.fromEntries(
    form.fields.flatMap((f) => (missing.includes(f.id) ? [[f.id, t.requiredAnswer]] : fieldError(submit.error, f.id) ? [[f.id, t.invalidAnswer]] : [])),
  )
  const fieldProblem = ['FullName', 'Email', ...form.fields.map((f) => f.id)].some((k) => fieldError(submit.error, k))

  return (
    <Paper withBorder p="lg" shadow="sm">
      <form
        onSubmit={(e) => {
          e.preventDefault()
          // Choice groups have no native "required" check – do it here.
          const empty = form.fields.filter((f) => f.required && !(answers[f.id] ?? []).some((v) => v.trim())).map((f) => f.id)
          setMissing(empty)
          if (empty.length) document.getElementById(`q-${empty[0]}`)?.scrollIntoView({ behavior: 'smooth', block: 'center' })
          else submit.mutate()
        }}
      >
        <Stack>
          {submit.isError && !fieldProblem && <Alert color="red">{submit.error instanceof ApiError && submit.error.status === 410 ? t.formClosed : t.somethingWrong}</Alert>}
          <TextInput label={t.fullName} required autoComplete="name" value={person.fullName} error={invalid('FullName', t.invalidName)} onChange={(e) => setPerson({ ...person, fullName: e.currentTarget.value })} />
          <TextInput label={t.email} type="email" required autoComplete="email" value={person.email} error={invalid('Email', t.invalidEmail)} onChange={(e) => setPerson({ ...person, email: e.currentTarget.value })} />
          <FormFields fields={form.fields} sections={form.sections} answers={answers} onChange={(a) => (setAnswers(a), setMissing([]))} errors={errors} />
          <Checkbox required checked={person.gdprConsent} onChange={(e) => setPerson({ ...person, gdprConsent: e.currentTarget.checked })} label={t.consent} />
          <Anchor component="button" type="button" size="sm" onClick={() => setPrivacyOpen((o) => !o)} style={{ alignSelf: 'flex-start' }}>
            {t.privacyLink}
          </Anchor>
          <Collapse expanded={privacyOpen}>
            <Text size="sm" c="dimmed">
              {t.formPrivacy(form.contactEmail)}
            </Text>
          </Collapse>
          <Button type="submit" loading={submit.isPending}>
            {t.submit}
          </Button>
          {form.contactEmail && (
            <Text size="sm" c="dimmed">
              {t.formProblemHelp} <Anchor href={`mailto:${form.contactEmail}`}>{form.contactEmail}</Anchor>
            </Text>
          )}
        </Stack>
      </form>
    </Paper>
  )
}
