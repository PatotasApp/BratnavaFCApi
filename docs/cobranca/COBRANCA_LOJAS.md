# Cobrança da patota pelas lojas

Cobrança pelo uso do PatotasApp **por patota**, com Apple In-App Purchase e Google Play Billing.

## Modelo

- **Pacote avulso que soma meses**: 1, 3, 6 ou 12 meses (`patota_1m`, `patota_3m`, `patota_6m`, `patota_12m`).
  - Apple: tipo **Assinatura não renovável** (*Non-Renewing Subscription*).
  - Google: **produto no app (avulso) consumível**. O servidor consome a compra; o Google exige isso em até 3 dias.
- Cada pacote soma a partir do vencimento atual, então dá para **pagar antes de vencer**. Se a patota já venceu, conta a partir do pagamento. Pagar durante o teste grátis soma depois do fim do teste.
- **Qualquer admin ou financeiro** da patota paga. O **jogador comum não vê nada** de cobrança: a API responde 403 e o app esconde o card e a faixa.
- **Teste grátis no servidor**: 30 dias a partir da criação da patota (parametrizável), **um por usuário criador**. A regra vale mesmo que a patota seja apagada e recriada.
- **Bratnava FC (`3f401edf-…`) é vitalícia.** A migration já grava isso.
- Depois de vencer vem a **tolerância** (padrão 3 dias). Depois dela, **somente leitura**: a API responde 402 com `code: PATOTA_VENCIDA`, mas só com o **interruptor ligado**. A migration cria o interruptor **desligado**.
- Toda compra é **conferida no servidor**:
  - Apple: App Store Server API + JWS com cadeia x5c até o "Apple Root CA - G3".
  - Google: Google Play Developer API.
  - O ID da transação é único: a mesma compra nunca soma duas vezes.
  - **Reembolso tira exatamente aquele pacote**: a fila de períodos é recalculada do zero.
- **Lembretes por push** para admins e financeiros 7, 3 e 1 dia antes e no dia (job `billing-reminders`, 9h de Brasília). Eles começam desligados no painel.
- A **administração fica só no site**, em `/app/admin/godmode` → aba **Cobrança**:
  - resumo e lista por patota com histórico;
  - estender dias (com motivo, fica na auditoria), liberar como vitalícia, reprocessar compra com a loja;
  - configurações e produto ativo por loja;
  - problemas: avisos com erro, compras do Google sem consumo, pendentes e reembolsos.

### Endpoints

| Quem | Rota |
|---|---|
| App (admin/financeiro) | `GET /api/billing/groups/{groupId}?platform=android\|ios` · `POST /api/billing/groups/{groupId}/purchases` · `POST /api/billing/restore` |
| Lojas (anônimo, conferido) | `POST /api/billing/webhooks/apple` · `POST /api/billing/webhooks/google?token=<segredo>` |
| GodMode (site) | `/api/admin/billing/*`: `dashboard`, `groups`, `groups/{id}`, `groups/{id}/extend`, `groups/{id}/lifetime`, `purchases/reprocess`, `purchases/{id}/resync`, `purchases/{id}/simulate`, `settings`, `products`, `problems`, `events/{id}/retry` |

### Secrets no Fly (produção)

```
Billing__Apple__BundleId=br.com.patotasapp
Billing__Apple__IssuerId=<Issuer ID>
Billing__Apple__KeyId=<Key ID da chave de compras no app>
Billing__Apple__PrivateKey=<conteúdo do .p8, com \n no lugar das quebras de linha>
Billing__Google__PackageName=br.com.patotasapp
Billing__Google__ServiceAccountJson=<JSON inteiro da conta de serviço>
Billing__Google__PubSubToken=<segredo longo e aleatório>
```

Sem esses secrets a loja aparece como "sem credenciais" no painel, e a compra é recusada com mensagem clara. **Nunca** ligue `Billing__Simulated__Enabled` fora do Development: a API ignora a opção em qualquer outro ambiente.

## O que configurar em cada console (Luis)

### App Store Connect (conta de pessoa física)

1. **Contratos, impostos e dados bancários**: aceite o contrato de **Apps pagos**, cadastre a conta bancária e preencha o formulário fiscal (W-8BEN, pessoa física no Brasil). Sem isso as compras não aparecem.
2. **App → Monetização → Compras dentro do app → (+)**. Crie quatro do tipo **Assinatura não renovável**:

   | ID do produto | Nome de referência | Preço sugerido (BRL) |
   |---|---|---|
   | `patota_1m` | Plano da patota – 1 mês | R$ 29,90 |
   | `patota_3m` | Plano da patota – 3 meses | R$ 84,90 (5% de desconto) |
   | `patota_6m` | Plano da patota – 6 meses | R$ 166,90 (7% de desconto) |
   | `patota_12m` | Plano da patota – 12 meses | R$ 322,90 (10% de desconto) |

   Em cada uma:
   - Localização pt-BR. Nome: "Plano da patota – N meses". Descrição: "Libera todos os recursos da patota por N meses. Pagamento único, sem renovação automática; soma ao vencimento atual."
   - Captura de tela da tela "Plano da patota".
   - Nota para a revisão: "A cobrança é por patota (grupo). O admin ou financeiro paga em Minha patota → Plano da patota. O histórico fica no nosso servidor e vale em qualquer aparelho, já que a compra fica ligada à patota. O botão Restaurar compras está na mesma tela." Inclua uma conta de teste que seja admin de uma patota.
3. **Usuários e acesso → Integrações → Compras dentro do app**: gere uma chave. Anote o **Issuer ID** e o **Key ID** e baixe o **.p8** (só dá para baixar uma vez). Eles viram os secrets `Billing__Apple__*`.
4. **App → Informações do app → Notificações do servidor da App Store**: versão **2**. URL de produção: `https://<api-prod>/api/billing/webhooks/apple`. URL de sandbox: `https://<api-dev>/api/billing/webhooks/apple`. Depois use "Enviar notificação de teste": ela aparece em StoreEvents como `TEST`, com status Ignored.
5. **Usuários e acesso → Sandbox**: crie testadores para o TestFlight.
6. No Xcode (precisa de Mac/CI macOS): capability **In-App Purchase** no target Runner. Hoje esse é o bloqueio para o iOS.

### Google Play Console (conta de pessoa física)

1. **Configurar → Perfil de pagamentos**: crie e vincule a conta de comerciante (dados bancários).
2. **Monetizar com o Play → Produtos → Produtos no app**: crie `patota_1m`, `patota_3m`, `patota_6m` e `patota_12m` com os mesmos preços e descrições. Ative todos. O app trata os quatro como consumíveis, e quem consome é o servidor.
3. **Conta de serviço**:
   - No Google Cloud do projeto do Play, ative a **Google Play Android Developer API**.
   - Crie uma conta de serviço e uma chave JSON (vira o `Billing__Google__ServiceAccountJson`).
   - No Play Console, em **Usuários e permissões**, convide o e-mail da conta de serviço com "Ver dados financeiros" e "Gerenciar pedidos e assinaturas", só para este app.
4. **Notificações em tempo real (RTDN)**:
   - No Google Cloud → Pub/Sub, crie o tópico `play-billing`.
   - No tópico, dê o papel **Publicador do Pub/Sub** a `google-play-developer-notifications@system.gserviceaccount.com`.
   - Crie uma assinatura **push** com endpoint `https://<api>/api/billing/webhooks/google?token=<segredo>`. O segredo é o mesmo do `Billing__Google__PubSubToken`.
   - No Play Console, em **Monetizar → Configuração da monetização → Notificações do desenvolvedor em tempo real**, informe `projects/<projeto>/topics/play-billing` e envie a notificação de teste.
5. **Configuração → Teste de licença**: adicione os e-mails dos testadores, que compram sem pagar de verdade. Publique numa faixa de **teste interno**: as compras só funcionam em build instalado pelo Play. O flavor `dev` usa `br.com.patotasapp.dev` e não acha esses produtos; para testar localmente use a compra simulada.
6. **Política → Conteúdo do app → Segurança dos dados**: declare "Compras" no histórico de compras.

## Teste local com compra simulada

Ambiente separado das outras frentes: banco `bratnavafc_demo_lojas` (container `bratnavafc-dev-championship`, porta 5433), API na **5241**, site na **5181**.

A "Resenha FC" do banco demo usa o ID da Bratnava e por isso nasce **vitalícia**. Para testar o pagamento nela, abra o GodMode no site, vá em Cobrança → Patotas → Resenha FC e clique em "Remover vitalício" (pede um motivo). Para ver o bloqueio:
- ligue o **Bloqueio** em Cobrança → Configurações;
- deixe a patota vencer: no banco demo, crie uma patota nova com outro usuário criador ou zere os dias de teste;
- tente criar algo com um usuário **que não seja GodMode**, porque o GodMode nunca é bloqueado.

API (PowerShell):

```powershell
$base = ((dotnet user-secrets list --id 64fa5fc7-1145-4ee8-8674-e978908e11b0) -match '^DatabaseCopy:DevelopmentConnection = ') -replace '^DatabaseCopy:DevelopmentConnection = ',''
$env:ConnectionStrings__DefaultConnection = $base -replace 'Database=[^;]*','Database=bratnavafc_demo_lojas'
$env:FIREBASE_SERVICE_ACCOUNT_JSON_PATH = 'C:\Users\lhnme\Downloads\development-d04ef-firebase-adminsdk-fbsvc-e091926bdd.json'
$env:Billing__Simulated__Enabled = 'true'
cd C:\Users\lhnme\source\repos\BratnavaFCApi-lojas-cobranca\src\BratnavaFC.Api
dotnet run --launch-profile http --urls http://localhost:5241
```

Site (outro terminal):

```powershell
cd C:\Users\lhnme\source\repos\BratnavaFCFront-lojas-cobranca
$env:VITE_API_URL = 'http://localhost:5241/'
npm run dev -- --port 5181
```

Abra `http://localhost:5181/app/admin/godmode` → aba **Cobrança**.

App no emulador (outro terminal). O arquivo `config/lojas-local.json` fica fora do git:

```powershell
cd C:\Users\lhnme\source\repos\BratnavaFCApp-lojas-cobranca
flutter run --flavor dev --dart-define-from-file=config/lojas-local.json
```

O que testar no app, logado como admin da patota:
- **Minha patota → Plano da patota**: situação, vencimento e dias.
- **Pagar mais meses**: na seção "Compra simulada" escolha um pacote e confirme. O vencimento soma, e o histórico mostra quem pagou e o período coberto.
- Pague de novo antes de vencer: soma ao fim do pacote anterior.
- **Dashboard**: a faixa aparece com 7 dias ou menos. No GodMode, em Configurações, reduza os dias de teste de uma patota nova para ver a faixa.
- Pelo site, use **Simular reembolso** num pacote: o período sai, e o app mostra o novo vencimento ao recarregar.
- Com um jogador comum, nada de cobrança aparece. Com o bloqueio ligado e a patota vencida, qualquer ação de escrita mostra o aviso "Patota em modo somente leitura".

A migration é aplicada com `--connection` explícito, **nunca** sem ele (a DefaultConnection é produção):

```powershell
dotnet ef database update --project src/BratnavaFC.Infrastructure --startup-project src/BratnavaFC.Api --connection "<connection do banco de destino>"
```
