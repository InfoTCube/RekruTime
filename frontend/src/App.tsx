import { Badge, Container, Group, Title } from '@mantine/core'
import { useQuery } from '@tanstack/react-query'
import { api } from './api'

function App() {
  const health = useQuery({ queryKey: ['health'], queryFn: () => api<{ status: string }>('/api/health') })

  return (
    <Container py="xl">
      <Title mb="md">RekruTime</Title>
      <Group>
        API:
        {health.isPending && <Badge color="gray">checking…</Badge>}
        {health.isSuccess && <Badge color="green">{health.data.status}</Badge>}
        {health.isError && <Badge color="red">unavailable</Badge>}
      </Group>
    </Container>
  )
}

export default App
