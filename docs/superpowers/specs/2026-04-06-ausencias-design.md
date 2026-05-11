# Design: Gerenciamento de Ausências — BratnavaFC

**Data:** 2026-04-06  
**Status:** Aprovado

---

## Visão Geral

Permite que usuários cadastrem períodos de ausência (viagem, médico, pessoal, outros). Um BackgroundService verifica diariamente se há partidas futuras coincidindo com ausências e rejeita automaticamente o convite do jogador. Na lista de rejeitados da partida, um ícone com tooltip indica o motivo quando a rejeição foi automática.

---

## Domínio / Backend

### Enum `AbsenceType`

```csharp
// BratnavaFC.Domain/Enums/AbsenceType.cs
public enum AbsenceType
{
    Travel            = 1,  // ícone: lucide:Plane
    MedicalDepartment = 2,  // ícone: lucide:Cross (vermelho)
    Personal          = 3,  // ícone: lucide:HeartHandshake
    Other             = 4,  // ícone: lucide:CircleEllipsis
}
```

Ícones são resolvidos exclusivamente no frontend. Nenhum dado de ícone é persistido no banco.

---

### Entidade `UserAbsenceEntity`

```csharp
// BratnavaFC.Domain/Entities/UserAbsenceEntity.cs
public class UserAbsenceEntity : BaseEntity
{
    public Guid UserId { get; private set; }
    public UserEntity? User { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }   // inclusivo

    public AbsenceType AbsenceType { get; private set; }
    public string? Description { get; private set; } // opcional
}
```

- Ligada ao `UserEntity` (não ao `PlayerEntity`) — cobre todos os grupos do usuário.
- `EndDate` é inclusivo: uma ausência de 10/04 a 10/04 cobre o dia 10/04.

---

### Alteração em `MatchPlayerEntity`

Campos adicionados:

```csharp
public Guid? AutoRejectedByAbsenceId { get; private set; }
public UserAbsenceEntity? AutoRejectedByAbsence { get; private set; }
```

Método auxiliar:

```csharp
public void AutoRejectByAbsence(Guid absenceId)
{
    InviteResponse = InviteResponse.Rejected;
    AutoRejectedByAbsenceId = absenceId;
}
```

- `AutoRejectedByAbsenceId == null` → rejeição manual (não exibe ícone no frontend).
- `AutoRejectedByAbsenceId != null` → rejeição automática (exibe ícone + tooltip).
- Se o usuário deletar ou editar a ausência após a rejeição automática, o convite **permanece rejeitado**. O jogador deve aceitar manualmente se quiser participar.

---

### BackgroundService `AbsenceAutoRejectBackgroundService`

- Herda de `BackgroundService` (.NET hosted service).
- Executa uma vez por dia às **00:00 UTC**.
- Lógica:
  1. Busca todas as `MatchEntity` com `Status == Created` e `PlayedAt.Date >= DateTime.UtcNow.Date`.
  2. Para cada `MatchPlayer` com `InviteResponse != Rejected`:
     - Verifica se `Player.UserId` possui alguma `UserAbsenceEntity` onde `StartDate <= playedAtDate <= EndDate`.
     - Se sim → chama `AutoRejectByAbsence(absence.Id)` e salva.
- Registrado via `builder.Services.AddHostedService<AbsenceAutoRejectBackgroundService>()` no `Program.cs`.

---

### Alteração em `PlayerInMatchDto`

Dois campos adicionados:

```csharp
public int? AbsenceType { get; set; }           // null = rejeição manual ou não-rejeitado
public string? AbsenceDescription { get; set; } // ex: "Viagem - Férias em SP" ou "Viagem"
```

- Preenchidos apenas quando `AutoRejectedByAbsenceId != null`.
- `AbsenceDescription` é montada no backend: `"{TypeName}"` ou `"{TypeName} - {description}"`.

---

## API Endpoints

**Controller:** `AbsencesController` — base `/api/absences`

| Método | Rota | Descrição | Autorização |
|--------|------|-----------|-------------|
| `GET` | `/api/absences/mine` | Lista ausências do usuário logado | User+ |
| `POST` | `/api/absences` | Cria ausência | User+ |
| `PUT` | `/api/absences/{id}` | Edita ausência própria | User+ |
| `DELETE` | `/api/absences/{id}` | Remove ausência própria | User+ |
| `GET` | `/api/absences/group/{groupId}` | Lista ausências dos jogadores do grupo | GroupAdmin+ |

### `CreateAbsenceDto`

```csharp
public record CreateAbsenceDto(
    DateOnly StartDate,
    DateOnly EndDate,
    AbsenceType AbsenceType,
    string? Description
);
```

Validações:
- `StartDate <= EndDate`
- `AbsenceType` deve ser valor válido do enum

### `AbsenceDto` (resposta)

```csharp
public record AbsenceDto(
    Guid Id,
    DateOnly StartDate,
    DateOnly EndDate,
    int AbsenceType,
    string AbsenceTypeName,
    string? Description,
    DateTime CreatedAt
);
```

---

## Frontend

### Rota e menu

- Nova rota em `App.tsx`: `<Route path="absences" element={<AbsencesPage />} />`
- Item no `Sidebar.tsx` (visível para todos, abaixo de "Calendário"):
  ```ts
  { to: "/app/absences", label: "Ausências", icon: CalendarOff }
  ```

---

### `AbsencesPage.tsx`

Página com duas seções:

**1. Formulário de cadastro/edição**
- Data início + data fim (inputs `date`)
- Select de tipo com ícone ao lado de cada opção
- Textarea de descrição (opcional)
- Botão salvar

**2. Lista de ausências**
- Cards com: ícone do tipo (DM em vermelho), período `dd/mm/yyyy até dd/mm/yyyy`, descrição opcional
- Botões editar / excluir por card

---

### `src/lib/absenceIcons.ts`

```ts
export const ABSENCE_ICONS: Record<number, string> = {
  1: "lucide:Plane",           // Viagem
  2: "lucide:Cross",           // Departamento Médico
  3: "lucide:HeartHandshake",  // Pessoal
  4: "lucide:CircleEllipsis",  // Outros
};

export function resolveAbsenceIcon(absenceType: number): string {
  return ABSENCE_ICONS[absenceType] ?? "lucide:CircleEllipsis";
}
```

---

### Alteração em `InviteList.tsx` — `PlayerRow`

Na lista de **rejeitados**, quando `p.absenceType != null`, exibir inline ao lado do nome:

```tsx
{p.absenceType != null && (
  <span
    title={p.absenceDescription}
    className="leading-none opacity-80 cursor-help"
  >
    <IconRenderer
      value={resolveAbsenceIcon(p.absenceType)}
      size={14}
      lucideProps={p.absenceType === 2 ? { color: "red" } : undefined}
    />
  </span>
)}
```

Tooltip nativo (`title`) exibe ex: `"Viagem - Férias em SP"` ou só `"Viagem"`.

---

## Migração de banco

Nova migration EF Core cobrindo:
1. Tabela `UserAbsences` (`Id`, `UserId` FK, `StartDate`, `EndDate`, `AbsenceType` short, `Description`, `CreateDate`, `UpdateDate`)
2. Coluna `AutoRejectedByAbsenceId` (nullable FK) na tabela `MatchPlayers`
3. Índice em `UserAbsences(UserId, StartDate, EndDate)` para otimizar a query do BackgroundService

---

## Fora do escopo

- Notificação push ao ser auto-rejeitado (pode ser adicionada futuramente)
- Reabertura automática do convite ao deletar ausência
- Visibilidade de ausências entre membros comuns do mesmo grupo
