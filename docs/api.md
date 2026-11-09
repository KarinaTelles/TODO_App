# Contrato da API

Base: `http://localhost:5000` em desenvolvimento. Todo corpo é JSON (`Content-Type: application/json`).
Campos em camelCase, enums em minúsculas, datas de prazo como `AAAA-MM-DD` e instantes em ISO 8601 UTC
(`2026-11-09T15:00:00Z`).

Erros seguem ProblemDetails (RFC 9457), com `Content-Type: application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Tarefa não encontrada",
  "status": 404,
  "detail": "Não existe tarefa com id 7.",
  "traceId": "00-..."
}
```

Erros de validação (400) trazem `errors`, um objeto de campo para lista de mensagens:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "title": ["O título é obrigatório."] }
}
```

O documento OpenAPI gerado pela própria API fica em `GET /openapi/v1.json`.

## Informações do servidor

### `GET /api/info`

Diz se o assistente está usando o modelo de verdade ou o modo demonstração. A interface deve mostrar
claramente quando `demoMode` for `true`: nesse modo não há IA, só um interpretador de comandos fixos.

```json
{
  "mode": "demo",
  "model": null,
  "demoMode": true,
  "pendingActionMinutes": 10,
  "maxBulkDelete": 10,
  "readTools": ["list_tasks", "get_task"],
  "writeTools": ["create_task", "update_task", "complete_task", "delete_task"]
}
```

Com `ANTHROPIC_API_KEY` configurada: `"mode": "anthropic"`, `"model": "claude-opus-5-5"`, `"demoMode": false`.

Comandos aceitos no modo demonstração (também devolvidos como texto de ajuda quando o comando não é reconhecido):

| Comando | Ferramenta |
| --- | --- |
| `listar`, `listar concluídas`, `listar todas` | `list_tasks` |
| `ver 3` | `get_task` |
| `criar "pagar boleto" para amanhã` (também `hoje`, `depois de amanhã`, `15/11`, `15/11/2027`, ou sem prazo) | `create_task` |
| `concluir 3` | `complete_task` |
| `adiar 3 para 20/11` | `update_task` |
| `excluir 3` ou `excluir 3, 4 e 5` | `delete_task` |

### `GET /healthz`

`200 {"status":"ok"}`.

## Tarefas

### Objeto `Task`

```json
{
  "id": 1,
  "title": "Pagar boleto",
  "notes": null,
  "dueDate": "2026-11-10",
  "priority": "normal",
  "done": false,
  "createdAt": "2026-11-09T15:00:00Z",
  "updatedAt": "2026-11-09T15:00:00Z",
  "version": 1
}
```

- `priority`: `low`, `normal` ou `high`.
- `dueDate` e `notes` podem ser `null`.
- `version` começa em 1 e sobe a cada alteração. Serve para detectar edição concorrente.
- Limites: `title` de 1 a 200 caracteres (espaços nas pontas são removidos), `notes` até 2000.

### `GET /api/tasks`

Parâmetros de query, todos opcionais:

| Nome | Valores | Efeito |
| --- | --- | --- |
| `status` | `all` (padrão), `open`, `done` | filtra por situação |
| `search` | texto | busca no título e nas notas |
| `dueBefore` | `AAAA-MM-DD` | só tarefas com prazo até essa data (inclusive) |
| `priority` | `low`, `normal`, `high` | filtra por prioridade |

Resposta `200`: `Task[]`, ordenada por: abertas primeiro, prazo mais próximo, sem prazo por último,
prioridade maior, id. Valor inválido em `status` ou `priority` dá `400`.

### `GET /api/tasks/{id}`

`200 Task` ou `404`.

### `POST /api/tasks`

```json
{ "title": "Pagar boleto", "notes": null, "dueDate": "2026-11-10", "priority": "normal" }
```

Só `title` é obrigatório; `priority` padrão é `normal`. Resposta `201 Task` com `Location: /api/tasks/{id}`,
ou `400`.

### `PUT /api/tasks/{id}`

Substitui a tarefa inteira. `version` é obrigatório e deve ser o valor lido.

```json
{
  "title": "Pagar boleto",
  "notes": "banco do bairro",
  "dueDate": "2026-11-12",
  "priority": "high",
  "done": false,
  "version": 1
}
```

- `200 Task` (com `version` incrementada).
- `400` se faltar `title` ou `version`.
- `404` se a tarefa não existe.
- `409` se `version` não é a atual. O corpo traz `currentVersion`:

```json
{
  "title": "Versão desatualizada",
  "status": 409,
  "detail": "A tarefa 1 foi alterada depois da sua leitura. Versão atual: 2.",
  "currentVersion": 2
}
```

`priority` omitido mantém o valor atual. `notes` e `dueDate` omitidos viram `null`.

### `DELETE /api/tasks/{id}?version=N`

`version` é opcional; se enviado e diferente do atual, `409`. Resposta `204`, `404` ou `409`.

## Assistente

O assistente conversa e usa ferramentas. Ferramentas de leitura (`list_tasks`, `get_task`) rodam na hora.
Ferramentas de escrita (`create_task`, `update_task`, `complete_task`, `delete_task`) nunca rodam sozinhas:
viram uma ação pendente (`PendingAction`) que o usuário confirma ou recusa na tela. A prévia (`preview`)
é calculada pelo servidor a partir dos dados atuais, não escrita pelo modelo.

### Objeto `PendingAction`

```json
{
  "id": "6d1d654d-c19d-428b-84d0-ad0d12200655",
  "conversationId": "15847ac6-830a-4e63-9112-11a5cc089181",
  "tool": "delete_task",
  "preview": "Excluir 2 tarefas: #1 \"Pagar aluguel\"; #3 \"Consulta médica\".",
  "targets": [
    { "id": 1, "title": "Pagar aluguel", "version": 1 },
    { "id": 3, "title": "Consulta médica", "version": 2 }
  ],
  "arguments": { "ids": [1, 3] },
  "status": "pending",
  "createdAt": "2026-11-09T15:00:00Z",
  "expiresAt": "2026-11-09T15:10:00Z",
  "resolvedAt": null,
  "resultIsError": null,
  "result": null
}
```

- `tool`: uma das quatro ferramentas de escrita.
- `targets`: tarefas afetadas e a versão de cada uma no momento da proposta. Vazio em `create_task`.
- `arguments`: argumentos normalizados pelo servidor. Formato por ferramenta:
  - `create_task`: `{ "title", "notes", "dueDate", "priority" }`
  - `update_task`: `{ "id", "title", "notes", "dueDate", "priority" }` (campo `null` = não muda)
  - `complete_task`: `{ "id" }`
  - `delete_task`: `{ "ids": [..] }` (no máximo `maxBulkDelete`)
- `status`: `pending`, `executed`, `rejected`, `expired`, `stale` (a tarefa mudou depois da proposta; nada
  foi feito) ou `failed` (a releitura depois da execução não bateu; a alteração foi desfeita).
- `result`: depois de resolvida, o mesmo JSON que o modelo recebe como resultado da ferramenta, por exemplo
  `{"status":"executed","summary":"...","task":{...}}`, `{"status":"rejected","message":"..."}`,
  `{"status":"stale","message":"..."}`. `resultIsError` é `false` só quando executou.
- Títulos em `preview` e `targets` são texto do usuário: a interface deve exibir como texto, nunca como HTML.

### `POST /api/chat`

```json
{ "conversationId": null, "message": "o que vence esta semana?" }
```

`conversationId` `null` ou ausente começa uma conversa nova. `message` de 1 a 4000 caracteres.

Resposta `200 ChatResponse`:

```json
{
  "conversationId": "15847ac6-830a-4e63-9112-11a5cc089181",
  "mode": "anthropic",
  "messages": [
    { "role": "assistant", "text": "Encontrei 2 tarefas. Quer que eu exclua a #3?" }
  ],
  "steps": [
    { "tool": "list_tasks", "kind": "read", "outcome": "executed" },
    { "tool": "delete_task", "kind": "write", "outcome": "proposed" }
  ],
  "pendingActions": [ { "...": "PendingAction" } ]
}
```

- `messages`: só as mensagens novas do assistente nesta chamada (a do usuário não é repetida). Pode vir vazio
  quando o modelo só propôs uma ação.
- `steps`: o que aconteceu com cada chamada de ferramenta. `kind`: `read`, `write` ou `unknown`. `outcome`:
  `executed`, `proposed` ou `error` (argumentos inválidos, id inexistente, mais ids que o limite; nada é
  proposto nesse caso e o erro volta ao modelo).
- `pendingActions`: ações que aguardam o usuário. Enquanto houver alguma, a conversa fica parada.

Erros: `400` (mensagem vazia ou longa), `404` (conversa não existe), `409` (há ação pendente nesta conversa;
confirme ou recuse antes). Uma ação pendente que já expirou não bloqueia: a próxima mensagem a encerra como
`expired`.

### `POST /api/actions/{id}/confirm`

Sem corpo. Executa a ação se ela ainda estiver `pending`, não expirada, e se cada tarefa alvo ainda tiver a
mesma `version`. Depois relê as tarefas para conferir o resultado.

Resposta `200 ActionResolution`:

```json
{
  "action": { "...": "PendingAction com status final" },
  "chat": { "...": "ChatResponse com a continuação do assistente" }
}
```

- `action.status` pode ser `executed`, `stale`, `failed` ou `expired`. Os quatro são `200`: o resultado
  vai ao modelo, que continua a conversa.
- `chat` é `null` quando outra ação do mesmo turno ainda está pendente. O assistente só continua quando todas
  forem resolvidas, e recebe todos os resultados juntos.
- `404` se a ação não existe.
- `409` se a ação já foi resolvida antes (confirmação dupla, ou confirmação depois de recusa). Nada é
  executado de novo. O corpo é ProblemDetails com a ação atual em `action`:

```json
{
  "title": "Ação já resolvida",
  "status": 409,
  "detail": "Esta ação já está com situação 'executed'. Nada foi feito.",
  "action": { "...": "PendingAction" }
}
```

### `POST /api/actions/{id}/reject`

Sem corpo. Marca como `rejected` e devolve o mesmo formato `ActionResolution`. `409` se já resolvida,
`404` se não existe.

### `GET /api/actions/{id}`

`200 PendingAction` ou `404`.

### `GET /api/conversations/{id}`

Para recarregar a tela.

```json
{
  "id": "15847ac6-830a-4e63-9112-11a5cc089181",
  "mode": "demo",
  "messages": [
    { "role": "user", "text": "listar" },
    { "role": "assistant", "text": "1 tarefa(s):\n#1 Pagar boleto, prazo 10/11/2026" }
  ],
  "pendingActions": []
}
```

`messages` contém só textos de usuário e assistente; chamadas e resultados de ferramentas ficam de fora.
`pendingActions` traz apenas as que ainda estão `pending`. `404` se não existe.

## Fluxo típico na interface

1. `GET /api/info` para saber o modo e mostrar o aviso de demonstração.
2. `POST /api/chat`. Mostrar `messages`; para cada item de `pendingActions`, um cartão com `preview`,
   `expiresAt` e os botões Confirmar e Recusar.
3. Ao clicar, `POST /api/actions/{id}/confirm` ou `/reject`. Atualizar o cartão com `action.status` e, se
   `chat` vier preenchido, mostrar `chat.messages` e eventuais novas `chat.pendingActions`.
4. Recarregar a lista com `GET /api/tasks` depois de uma ação `executed`.
