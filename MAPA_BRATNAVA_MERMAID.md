# Mapa funcional Bratnava FC

Este documento organiza as funcionalidades atuais do sistema em diagramas Mermaid.

## 1. Visao macro

```mermaid
flowchart TD
    User["Usuario / Conta"] --> Auth["Autenticacao"]
    User --> Groups["Patotas / Grupos"]

    Groups --> Members["Jogadores / Membros"]
    Groups --> Settings["Configuracoes da patota"]
    Groups --> Matches["Partidas"]
    Groups --> Polls["Votacoes e eventos"]
    Groups --> Payments["Financeiro"]
    Groups --> Calendar["Calendario"]
    Groups --> Absences["Ausencias"]
    Groups --> Notifications["Notificacoes"]
    Groups --> Bets["Apostas"]

    Settings --> MatchScheduling["Agendamento de partidas"]
    Settings --> TeamColors["Cores dos times"]
    Settings --> PaymentRules["Regras financeiras"]
    Settings --> MvpRules["Regras de MVP"]
    Settings --> Icons["Icones da patota"]

    Members --> PlayerStats["Historico e estatisticas"]
    Members --> Birthdays["Aniversarios"]

    Matches --> Acceptation["Aceitacao / Recusa"]
    Matches --> MatchMaking["MatchMaking"]
    Matches --> InGame["Em jogo"]
    Matches --> PostGame["Pos-jogo"]
    Matches --> History["Historico"]
    Matches --> Replays["Replays"]
    Matches --> MatchCard["Card de Instagram"]
    Matches --> LinkedPoll["Votacao/evento vinculado"]

    Polls --> LinkedPoll
    Calendar --> PollEvents["Eventos de calendario"]
    Payments --> Transactions["Caixa / Transacoes"]
```

## 2. Plataformas e rotas principais

```mermaid
flowchart LR
    subgraph Site["Site - BratnavaFCFront"]
        SLogin["/login"]
        SRegister["/register"]
        SDashboard["/app"]
        SGroups["/app/groups"]
        SMatches["/app/matches"]
        SHistory["/app/history"]
        SMatchDetails["/app/history/:groupId/:matchId"]
        SCalendar["/app/calendar"]
        SSettings["/app/settings"]
        STeamColors["/app/team-colors"]
        SPayments["/app/payments"]
        SPolls["/app/polls"]
        SAbsences["/app/absences"]
        SReplays["/app/replays"]
        SBet["/app/bet"]
        SStats["/app/groups/:groupId/visual-stats"]
        SPlayerHistory["/app/groups/:groupId/player-history"]
        SSpotlight["/app/spotlight"]
        STeamBuilder["/app/team-builder"]
        SPublicClip["/public/clip/:clipId"]
        SPublicMatch["/public/match/:matchId"]
        SUsers["/app/admin/users"]
        SGodMode["/app/admin/godmode"]
    end

    subgraph App["App - BratnavaFCApp"]
        ALogin["/login"]
        ARegister["/register"]
        ADashboard["/app"]
        AMatches["/app/matches"]
        AGroups["/app/groups"]
        AHistory["/app/history"]
        AMatchDetails["/app/history/:groupId/:matchId"]
        ACalendar["/app/calendar"]
        ASettings["/app/settings"]
        ATeamColors["/app/team-colors"]
        APayments["/app/payments"]
        APolls["/app/polls"]
        ABirthdays["/app/birthdays"]
        AAbsences["/app/absences"]
        ABet["/app/bet"]
        AReplays["/app/replays"]
        ASpotlight["/app/spotlight"]
        APlayerHistory["/app/player-history"]
        AInvites["/app/invites"]
        ATeamBuilder["/app/team-builder"]
        AMembers["/app/admin/users"]
    end

    Site --> Api["BratnavaFCApi"]
    App --> Api
```

## 3. Backend por dominio

```mermaid
flowchart TD
    Api["API"] --> AuthC["AuthenticationController"]
    Api --> UsersC["UsersController"]
    Api --> GroupsC["GroupsController"]
    Api --> PlayersC["PlayersController"]
    Api --> SettingsC["GroupSettingsController"]
    Api --> MatchesC["MatchesController"]
    Api --> PollsC["PollsController"]
    Api --> PaymentsC["PaymentController"]
    Api --> TransactionsC["FinancialTransactionsController"]
    Api --> CalendarC["CalendarController"]
    Api --> AbsencesC["AbsencesController"]
    Api --> NotificationsC["NotificationsController"]
    Api --> PushC["PushController"]
    Api --> TeamColorC["TeamColorController"]
    Api --> TeamGenerationC["TeamGenerationController"]
    Api --> TeamBuilderC["TeamBuilderController"]
    Api --> BetC["BetController"]
    Api --> PublicReplaysC["PublicReplaysController"]
    Api --> MatchCardC["MatchCardController"]
    Api --> GodModeC["GodModeController"]

    AuthC --> AuthS["AuthenticationService"]
    UsersC --> UserS["UserService"]
    GroupsC --> GroupS["GroupService"]
    PlayersC --> PlayerS["PlayerService"]
    PlayersC --> MembershipS["PlayerMembershipService"]
    SettingsC --> SettingsS["GroupSettingsService"]
    MatchesC --> MatchS["MatchService"]
    PollsC --> PollS["PollService"]
    PaymentsC --> PaymentS["PaymentService"]
    TransactionsC --> TransactionS["FinancialTransactionService"]
    CalendarC --> CalendarS["CalendarService"]
    AbsencesC --> AbsenceS["AbsenceService"]
    NotificationsC --> NotificationS["NotificationService"]
    PushC --> PushS["PushService"]
    TeamColorC --> TeamColorS["TeamColorService"]
    TeamBuilderC --> TeamBuilderS["TeamBuilderService"]
    BetC --> BetS["BetService"]
    MatchCardC --> MatchCardS["MatchCardService"]
```

## 4. Permissoes

```mermaid
flowchart TD
    User["Usuario comum"] --> U1["Dashboard"]
    User --> U2["Aceitar/recusar partida"]
    User --> U3["Votar em votacoes/eventos"]
    User --> U4["Adicionar convidados quando permitido"]
    User --> U5["Ver pagamentos pessoais"]
    User --> U6["Marcar seus pagamentos"]
    User --> U7["Cadastrar ausencias"]
    User --> U8["Ver historico permitido"]
    User --> U9["Curtir/favoritar replays"]
    User --> U10["Sair da patota / excluir conta"]

    Admin["Admin da patota"] --> A1["Gerenciar grupo"]
    Admin --> A2["Gerenciar jogadores"]
    Admin --> A3["Gerenciar configuracoes"]
    Admin --> A4["Criar/editar/excluir partidas"]
    Admin --> A5["Avancar/voltar etapas"]
    Admin --> A6["Gerar e ajustar times"]
    Admin --> A7["Registrar gols e finalizar partida"]
    Admin --> A8["Criar/editar votacoes e eventos"]
    Admin --> A9["Vincular votacao/evento a partida"]
    Admin --> A10["Gerenciar replays"]

    Financeiro["Financeiro da patota"] --> F1["Gerenciar mensalidades"]
    Financeiro --> F2["Gerenciar cobrancas extras"]
    Financeiro --> F3["Gerenciar caixa"]
    Financeiro --> F4["Resolver alertas de saida com pendencias"]

    GodMode["GodMode"] --> G1["Administracao global"]
    GodMode --> G2["Notificar usuarios/grupos"]
    GodMode --> G3["Acesso administrativo ampliado"]
```

## 5. Ciclo de grupo e membros

```mermaid
flowchart TD
    CreateAccount["Criar conta"] --> Login["Login"]
    Login --> HasGroup{"Tem patota ativa?"}
    HasGroup -- "Nao" --> CreateOrInvite["Criar patota ou aceitar convite"]
    HasGroup -- "Sim" --> Dashboard["Dashboard da patota"]

    CreateOrInvite --> Group["Patota"]
    Group --> Settings["Configurar patota"]
    Group --> Invite["Convidar usuario"]
    Group --> Guest["Criar convidado/jogador sem usuario"]

    Invite --> PendingInvite["Convite pendente"]
    PendingInvite --> AcceptInvite["Aceita convite"]
    PendingInvite --> RejectInvite["Recusa convite"]
    AcceptInvite --> PlayerLinked["Usuario vinculado a jogador"]
    Guest --> LaterLink["Pode vincular usuario futuramente"]

    PlayerLinked --> LeaveGroup["Sair da patota"]
    LeaveGroup --> PendingDebts{"Tem pendencias?"}
    PendingDebts -- "Sim" --> PayBeforeExit["Oferece pagar antes de sair"]
    PendingDebts -- "Nao" --> ConvertToGuest["Vira convidado no historico"]
    PayBeforeExit --> ConvertToGuest
    ConvertToGuest --> KeepHistory["Historico da patota preservado"]

    PlayerLinked --> DeleteAccount["Excluir conta"]
    DeleteAccount --> ExitAllGroups["Sai das patotas"]
    ExitAllGroups --> KeepHistory
```

## 6. Configuracoes da patota

```mermaid
flowchart TD
    Settings["Configuracoes"] --> Limits["Min/max jogadores"]
    Settings --> DefaultMatch["Padroes de partida"]
    Settings --> Scheduling["Agendamento de partidas"]
    Settings --> Payments["Financeiro"]
    Settings --> Mvp["MVP"]
    Settings --> StatsVisibility["Visibilidade de estatisticas"]
    Settings --> Icons["Icones"]

    DefaultMatch --> DefaultPlace["Local padrao"]
    DefaultMatch --> DefaultDayTime["Dia/hora padrao"]

    Scheduling --> Manual["Manual"]
    Scheduling --> Recurring["Recorrente"]
    Manual --> ManualItems["Agenda partida por data/hora"]
    Recurring --> Trigger["Dia/hora em que o sistema cria"]
    Recurring --> UsesDefaults["Usa local/dia/hora padrao da partida"]

    Payments --> MonthlyFee["Mensalidade linha/goleiro"]
    Payments --> PaymentMode["Modo de pagamento"]
    Payments --> DueDay["Dia de vencimento"]

    Mvp --> TieRule["Regra de empate"]
    Mvp --> AutoFinalize["Auto-finalizar MVP apos horas"]
```

## 7. Fluxo de partida

```mermaid
stateDiagram-v2
    [*] --> Criar
    Criar --> Aceitacao: abrir aceitacao
    Aceitacao --> MatchMaking: quorum / admin avanca
    MatchMaking --> Jogo: iniciar partida
    Jogo --> Encerrada: encerrar partida
    Encerrada --> PosJogo: ir para pos-jogo
    PosJogo --> Finalizada: finalizar
    Finalizada --> [*]

    Aceitacao --> Criar: voltar etapa
    MatchMaking --> Aceitacao: voltar etapa
    Jogo --> MatchMaking: voltar etapa
    Encerrada --> Jogo: voltar etapa
    PosJogo --> Encerrada: voltar etapa
```

## 8. Detalhes da partida

```mermaid
flowchart TD
    CreateMatch["Criar partida"] --> SyncPlayers["Sincronizar jogadores ativos"]
    SyncPlayers --> InvitePlayers["Convites da partida"]
    InvitePlayers --> Accept["Aceitar"]
    InvitePlayers --> Reject["Recusar"]
    InvitePlayers --> Pending["Pendente"]
    InvitePlayers --> AddGuest["Adicionar convidado na partida"]
    InvitePlayers --> AbsenceAutoReject["Ausencia futura pode auto-recusar"]

    Accept --> MatchMaking["MatchMaking"]
    MatchMaking --> GenerateTeams["Gerar opcoes de times"]
    GenerateTeams --> TeamOptions["Opcoes geradas"]
    TeamOptions --> SetTeams["Setar times"]
    SetTeams --> ManualAdjust["Mover jogador / trocar jogadores"]
    ManualAdjust --> PersistTeams["Salvar ajuste no backend"]
    SetTeams --> TeamColors["Cores dos times"]

    TeamColors --> Start["Iniciar partida"]
    Start --> ReplayEvents["Publicar replay: gol/jogada"]
    Start --> GoalsLive["Registrar gols"]
    GoalsLive --> OwnGoal["Gol contra contabiliza para time oposto"]
    GoalsLive --> Assist["Assistencia opcional"]
    Start --> End["Encerrar partida"]

    End --> PostGame["Pos-jogo"]
    PostGame --> Score["Placar"]
    PostGame --> MvpVote["Votacao MVP"]
    PostGame --> Finalize["Finalizar partida"]
    Finalize --> History["Historico"]
    Finalize --> BetsResolution["Resolver apostas"]
    Finalize --> Replays["Replays no historico"]
```

## 9. Votacoes e eventos

```mermaid
flowchart TD
    Polls["Votacoes / Eventos"] --> CreatePoll["Criar votacao"]
    Polls --> CreateEvent["Criar evento"]

    CreatePoll --> Options["Opcoes"]
    CreatePoll --> MultipleChoice["Escolha unica ou multipla"]
    CreatePoll --> ShowVotes["Votos visiveis ou anonimos"]
    CreatePoll --> Deadline["Prazo"]

    CreateEvent --> RsvpOptions["Sim / Talvez / Nao"]
    CreateEvent --> EventData["Data, hora, local, icone, custo"]
    CreateEvent --> AllowGuests["Permitir convidados"]

    Options --> Vote["Usuario vota"]
    RsvpOptions --> Vote
    Vote --> VoteDate["Data do voto"]
    Vote --> Results["Resultado"]

    AllowGuests --> AddGuest["Adicionar convidado"]
    AddGuest --> Presence["Presencas"]

    Results --> Percent["Percentual considera votos de mensalistas"]
    Presence --> TotalPresence["Presencas consideram Sim + convidados"]

    Polls --> ManageResponses["Gerenciar respostas"]
    ManageResponses --> AdminVote["Admin altera voto/resposta"]
    ManageResponses --> VoteDateVisible["Mostra quando cada um votou"]

    Polls --> LinkToMatch["Vincular a partida"]
    LinkToMatch --> MatchView["Tela de partida mostra representacao fiel"]

    Polls --> Close["Encerrar"]
    Close --> Reopen["Reabrir"]
    Polls --> Delete["Excluir"]
```

## 10. Financeiro

```mermaid
flowchart TD
    Payments["Financeiro"] --> Monthly["Mensalidades"]
    Payments --> Extra["Cobrancas extras"]
    Payments --> MyPayments["Meus pagamentos"]
    Payments --> Cash["Caixa / Transacoes"]
    Payments --> ExitAlerts["Alertas de saida com pendencias"]

    Monthly --> InitiateMonth["Iniciar mes"]
    Monthly --> MonthlyGrid["Grade por jogador/mes"]
    Monthly --> MarkMonthlyPaid["Marcar pago"]
    Monthly --> UnmarkMonthlyPaid["Desmarcar pago"]
    Monthly --> PaymentProof["Comprovante"]

    Extra --> CreateExtra["Criar cobranca"]
    Extra --> ExtraPlayers["Selecionar jogadores"]
    Extra --> ExtraDiscount["Desconto individual ou em massa"]
    Extra --> MarkExtraPaid["Marcar pago"]
    Extra --> CancelExtra["Cancelar / reativar"]

    MarkMonthlyPaid --> Audit["Salva data e quem marcou"]
    MarkExtraPaid --> Audit
    UnmarkMonthlyPaid --> ClearAudit["Limpa data e marcado por"]

    MarkMonthlyPaid --> Cash
    MarkExtraPaid --> Cash
    Cash --> ManualEntry["Entrada/saida manual"]
    Cash --> Sync["Sincronizar pagamentos pagos"]

    ExitAlerts --> KeepPending["Manter pendencia"]
    ExitAlerts --> MarkAllPaid["Marcar tudo como pago"]
```

## 11. Calendario e ausencias

```mermaid
flowchart TD
    Calendar["Calendario"] --> ManualEvents["Eventos manuais"]
    Calendar --> MatchEvents["Partidas"]
    Calendar --> Birthdays["Aniversarios"]
    Calendar --> Holidays["Feriados"]
    Calendar --> Categories["Categorias"]

    ManualEvents --> CreateEvent["Criar/editar/excluir evento"]
    Categories --> ManageCategories["Criar/editar/excluir categorias"]

    Absences["Ausencias"] --> CreateAbsence["Cadastrar ausencia"]
    CreateAbsence --> DateRange["Inicio/fim"]
    CreateAbsence --> AbsenceType["Tipo: viagem, medico etc."]
    CreateAbsence --> AutoReject["Auto-recusa partidas futuras dentro do periodo"]
    Absences --> GroupAbsences["Admin ve ausencias do grupo"]
```

## 12. Historico, estatisticas e apostas

```mermaid
flowchart TD
    History["Historico"] --> MatchDetails["Detalhe da partida"]
    MatchDetails --> Score["Placar"]
    MatchDetails --> Lineup["Escalacao"]
    MatchDetails --> Goals["Gols"]
    MatchDetails --> Replays["Replays"]
    MatchDetails --> Mvp["Resultado MVP"]
    MatchDetails --> LinkedPoll["Votacao vinculada"]
    MatchDetails --> Simulation["Simulacao / linha do tempo"]

    Stats["Estatisticas"] --> VisualStats["Visual stats"]
    Stats --> PlayerHistory["Historico do jogador"]
    Stats --> Spotlight["Destaques"]
    Stats --> TeamBuilder["Montador/analisador de times"]

    Bets["Apostas"] --> BettableMatches["Partidas apostaveis"]
    Bets --> PlaceBet["Criar/alterar aposta"]
    Bets --> DeleteBet["Excluir aposta"]
    Bets --> Results["Resultado da aposta"]
    Bets --> Leaderboard["Ranking"]
    Bets --> Balance["Saldo"]
    Bets --> BetHistory["Historico de apostas"]

    MatchFinalized["Partida finalizada"] --> ResolveBets["Resolve apostas"]
    ResolveBets --> Results
    ResolveBets --> Balance
```

## 13. Replays

```mermaid
flowchart TD
    Match["Partida"] --> ReplayEvent["Publicar evento: Gol/Jogada"]
    ReplayEvent --> Worker["Worker/consumidor de replay"]
    Worker --> Clip["ReplayClip"]
    Clip --> Storage["R2 / Cloudflare"]
    Clip --> MatchReplays["Replays da partida"]
    Clip --> Vault["Replay Vault"]
    Clip --> PublicClip["Link publico do site"]

    MatchReplays --> Watch["Assistir video"]
    Vault --> Watch
    Watch --> Like["Curtir"]
    Watch --> Favorite["Favoritar"]
    Watch --> Share["Copiar link"]
    Watch --> Download["Download"]
    Watch --> Likers["Ver quem curtiu"]

    Like --> MyLikes["Meus likes"]
    Favorite --> MyFavorites["Meus favoritos"]
```

## 14. Notificacoes, realtime e jobs

```mermaid
flowchart TD
    Actions["Acoes do sistema"] --> Push["Push notifications"]
    Actions --> InApp["Notificacoes no app/site"]
    Actions --> Realtime["SignalR realtime"]
    Actions --> Scheduled["Jobs agendados"]

    Push --> Token["Push token do dispositivo"]
    InApp --> NotificationList["Lista de notificacoes"]
    NotificationList --> Read["Marcar lida"]
    NotificationList --> Delete["Excluir"]

    Realtime --> MatchChanged["match.changed"]
    Realtime --> PollChanged["poll.changed"]
    Realtime --> GroupChanged["group.changed"]

    Scheduled --> MatchReminders["Lembretes de partida"]
    Scheduled --> NoQuorum["Aviso sem quorum"]
    Scheduled --> MvpReminder["Lembrete MVP"]
    Scheduled --> MvpAutoFinalize["Auto-finalizar MVP"]
    Scheduled --> PollReminders["Lembretes de votacao/evento"]
    Scheduled --> PollAutoClose["Auto-encerrar votacao"]
    Scheduled --> CalendarReminders["Lembretes de calendario"]
    Scheduled --> MonthlyPaymentReminder["Lembrete mensalidade"]
    Scheduled --> BirthdayJob["Aniversarios"]
    Scheduled --> MatchScheduler["Criar partidas agendadas"]
    Scheduled --> ClipCleanup["Limpeza de clips"]
```

## 15. Relacionamento de entidades principais

```mermaid
erDiagram
    USER ||--o{ PLAYER : "possui/vincula"
    USER ||--o{ GROUP : "cria"
    GROUP ||--o{ PLAYER : "tem"
    GROUP ||--o{ GROUP_SETTINGS : "configura"
    GROUP ||--o{ GROUP_ADMIN : "admins"
    GROUP ||--o{ GROUP_FINANCEIRO : "financeiros"
    GROUP ||--o{ GROUP_INVITE : "convites"

    GROUP ||--o{ MATCH : "partidas"
    MATCH ||--o{ MATCH_PLAYER : "participantes"
    PLAYER ||--o{ MATCH_PLAYER : "entra em"
    MATCH ||--o{ GOAL : "gols"
    MATCH ||--o{ VOTE : "votos MVP"
    MATCH ||--o{ REPLAY_CLIP : "replays"
    POLL ||--o{ MATCH : "vinculada"

    GROUP ||--o{ POLL : "votacoes/eventos"
    POLL ||--o{ POLL_OPTION : "opcoes"
    POLL ||--o{ POLL_VOTE : "votos"
    POLL_VOTE ||--o{ POLL_GUEST : "convidados"
    PLAYER ||--o{ POLL_VOTE : "vota"

    GROUP ||--o{ MONTHLY_PAYMENT : "mensalidades"
    GROUP ||--o{ EXTRA_CHARGE : "cobrancas extras"
    EXTRA_CHARGE ||--o{ EXTRA_CHARGE_PAYMENT : "pagamentos"
    GROUP ||--o{ GROUP_TRANSACTION : "caixa"
    PLAYER ||--o{ MONTHLY_PAYMENT : "deve/paga"
    PLAYER ||--o{ EXTRA_CHARGE_PAYMENT : "deve/paga"
    GROUP ||--o{ EXIT_DEBT_ALERT : "alertas saida"

    GROUP ||--o{ CALENDAR_EVENT : "eventos"
    GROUP ||--o{ CALENDAR_CATEGORY : "categorias"
    USER ||--o{ USER_ABSENCE : "ausencias"

    REPLAY_CLIP ||--o{ REPLAY_LIKE : "likes"
    REPLAY_CLIP ||--o{ REPLAY_FAVORITE : "favoritos"
    USER ||--o{ USER_NOTIFICATION : "notificacoes"
    USER ||--o{ PUSH_TOKEN : "tokens"
```

## 16. Jornada principal do usuario

```mermaid
journey
    title Jornada principal na patota
    section Entrada
      Criar conta ou login: 5: Usuario
      Criar patota ou aceitar convite: 4: Usuario
      Selecionar patota ativa: 5: Usuario
    section Rotina
      Ver dashboard: 5: Usuario
      Confirmar partida: 5: Usuario
      Votar em eventos/votacoes: 4: Usuario
      Consultar financeiro: 3: Usuario
      Registrar ausencia: 4: Usuario
    section Admin
      Criar partida: 5: Admin
      Gerar times: 5: Admin
      Registrar gols/replays: 4: Admin
      Finalizar partida: 5: Admin
      Ajustar configuracoes: 3: Admin
    section Historico
      Ver resultado e MVP: 5: Usuario
      Assistir replays: 5: Usuario
      Curtir/favoritar videos: 4: Usuario
      Ver estatisticas: 4: Usuario
```
