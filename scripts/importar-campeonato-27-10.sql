-- Importa o campeonato de 27/10/2026 a partir das 3 partidas comuns já existentes.
-- Roteiro e decisões: docs/campeonato/IMPORTACAO-27-10.md.
--
-- Decisão do Luis (29/10): as 3 partidas originais NÃO são alteradas nem apagadas.
-- O campeonato ganha 3 jogos NOVOS, copiados delas. Depois de consolidado, as
-- originais são apagadas à parte. As originais estão como Ended (4) e nunca foram
-- finalizadas, então não contam em estatística: nada fica em dobro.
--
-- Garantias:
--   * Nada é apagado nem alterado: só INSERT (campeonato, times, participantes,
--     3 jogos novos e os jogadores deles).
--   * Pode rodar de novo: os ids são fixos e cada passo pula o que já foi feito.
--   * Tudo numa transação: qualquer trava que falhar desfaz tudo.
--   * Trava de entrada: se placar, cores ou status das partidas mudaram desde a
--     leitura de 29/10, aborta sem mexer em nada.
--
-- Pré-requisito: migrações do campeonato aplicadas (deploy da API com o campeonato).

BEGIN;

-- match_id: partida original (só lida); o jogo novo usa um id fixo derivado dele.
CREATE TEMP TABLE imp_jogos (match_id uuid PRIMARY KEY, ordem int, cor_a uuid, cor_b uuid, gols_a int, gols_b int,
                             jogo_id uuid GENERATED ALWAYS AS (md5('campeonato-27-10/jogo/' || match_id::text)::uuid) STORED) ON COMMIT DROP;
INSERT INTO imp_jogos VALUES
  ('a110819e-989d-425d-81ac-479cb7b6353e', 1, '7957a513-c002-47af-87d0-672ca3dddc1d', 'ceea3e45-832b-42dc-aa35-eeb6418de120', 4, 3), -- Laranja x Azul
  ('e8280212-2dfd-4055-ab3f-d7093a677928', 2, '5e63ae31-d7f3-474a-a767-3db5127fb968', 'ceea3e45-832b-42dc-aa35-eeb6418de120', 7, 3), -- Preto x Azul
  ('83100247-218e-4ce7-8d8e-06479bb7b7ea', 3, '5e63ae31-d7f3-474a-a767-3db5127fb968', '7957a513-c002-47af-87d0-672ca3dddc1d', 5, 4); -- Preto x Laranja

CREATE TEMP TABLE imp_times (team_id uuid PRIMARY KEY, nome text, cor uuid, ordem int) ON COMMIT DROP;
INSERT INTO imp_times VALUES
  ('aad22248-b8b8-584e-9823-046334d2a89a', 'Preto',   '5e63ae31-d7f3-474a-a767-3db5127fb968', 1),
  ('89e79b46-0680-5c15-871d-35e3d19fe97f', 'Laranja', '7957a513-c002-47af-87d0-672ca3dddc1d', 2),
  ('67d84b57-b8df-5afd-ab20-a4e785dc28e4', 'Azul',    'ceea3e45-832b-42dc-aa35-eeb6418de120', 3);

-- ── Trava de entrada ───────────────────────────────────────────────────────
DO $$
DECLARE
  campeonato constant uuid := 'bac18efd-395e-537c-8dff-122e7e273c57';
  ok int;
BEGIN
  SELECT count(*) INTO ok
  FROM "Matches" m JOIN imp_jogos j ON j.match_id = m."Id"
  WHERE m."GroupId" = '3f401edf-d309-4bae-97d8-28eae0da7a8a'
    AND m."TeamAColorId" = j.cor_a AND m."TeamBColorId" = j.cor_b
    AND m."TeamAGoals" = j.gols_a AND m."TeamBGoals" = j.gols_b
    AND m."Status" = 4 AND m."ChampionshipId" IS NULL;
  IF ok <> 3 THEN
    RAISE EXCEPTION 'Trava: as 3 partidas não estão como na leitura de 29/10 (conferidas: %). Nada foi alterado.', ok;
  END IF;

  SELECT count(DISTINCT mp."PlayerId") INTO ok
  FROM "MatchPlayers" mp JOIN imp_jogos j ON j.match_id = mp."MatchId"
  WHERE mp."Team" IN (1, 2);
  IF ok <> 15 THEN
    RAISE EXCEPTION 'Trava: esperava 15 jogadores com time, achei %. Nada foi alterado.', ok;
  END IF;
END $$;

-- ── Campeonato ─────────────────────────────────────────────────────────────
-- Regras do Luis: conta como uma única partida; Preto vitória, demais derrota;
-- MVP do campeonato com votação aberta (status 4, pós-jogo); sem gols individuais.
INSERT INTO "Championships" ("Id", "GroupId", "Name", "PlayedAt", "PlaceName", "Status", "StartedAt", "RulesJson", "CreateDate", "UpdateDate", "LinkedPollId")
SELECT 'bac18efd-395e-537c-8dff-122e7e273c57', '3f401edf-d309-4bae-97d8-28eae0da7a8a', 'Campeonato 27/10',
       '2026-10-27T20:30:00+00', 'Boca Jrs', 4,
       (SELECT min(m."ActualStartTime") FROM "Matches" m JOIN imp_jogos j ON j.match_id = m."Id"),
       '{"format": 1, "gameEnd": {"goalLimit": null, "timeLimitMinutes": 20}, "mvpMode": 2,
         "standings": {"mode": 1, "winPoints": 3, "drawPoints": 1, "tiebreakers": [1, 2, 3, 4, 5]},
         "resultMode": 2, "historyMode": 1, "winnerStays": null, "roundRobinTurns": 1, "placementResults": [1, 3],
         "includeInTeamDraw": false, "mvpCountsInGeneralStats": true, "goalsCountInGeneralStats": false,
         "minGamesForPlacementResult": 1}'::jsonb,
       now(), now(),
       -- a enquete do dia (da partida normal, a J3) fica ligada também ao campeonato
       (SELECT m."LinkedPollId" FROM "Matches" m WHERE m."Id" = '83100247-218e-4ce7-8d8e-06479bb7b7ea')
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO "ChampionshipTeams" ("Id", "ChampionshipId", "Name", "ColorId", "Order", "FinalPosition", "CreateDate")
SELECT t.team_id, 'bac18efd-395e-537c-8dff-122e7e273c57', t.nome, t.cor, t.ordem, NULL, now()
FROM imp_times t
ON CONFLICT ("Id") DO NOTHING;

-- Participantes: quem jogou (time 1 ou 2), no time da cor em que jogou. Id fixo por jogador.
INSERT INTO "ChampionshipPlayers" ("Id", "ChampionshipId", "PlayerId", "InviteResponse", "TeamId", "IsGoalkeeper", "DidNotPlay", "IsMvp", "CreateDate")
SELECT md5('bac18efd-395e-537c-8dff-122e7e273c57' || x.player_id::text)::uuid,
       'bac18efd-395e-537c-8dff-122e7e273c57', x.player_id, 3, x.team_id, x.goleiro, false, false, now()
FROM (
  SELECT mp."PlayerId" AS player_id,
         min(t.team_id::text)::uuid AS team_id,
         count(DISTINCT t.team_id) AS times,
         bool_or(mp."IsGoalkeeper") AS goleiro
  FROM "MatchPlayers" mp
  JOIN imp_jogos j ON j.match_id = mp."MatchId"
  JOIN imp_times t ON t.cor = CASE mp."Team" WHEN 1 THEN j.cor_a ELSE j.cor_b END
  WHERE mp."Team" IN (1, 2)
  GROUP BY mp."PlayerId"
) x
ON CONFLICT ("Id") DO NOTHING;

-- ── 3 jogos novos, copiados das partidas originais ─────────────────────────
-- Mesmo placar, cores, datas e horário de início. Status 6 (encerrado no campeonato).
INSERT INTO "Matches" ("Id", "GroupId", "PlayedAt", "PlaceName", "Status", "TeamAGoals", "TeamBGoals",
                       "TeamAColorId", "TeamBColorId", "ActualStartTime", "LinkedPollId", "CreateDate", "UpdateDate",
                       "ChampionshipId", "ChampionshipTeamAId", "ChampionshipTeamBId", "ChampionshipGameOrder",
                       "ChampionshipExternalWinnerTeamId")
SELECT j.jogo_id, m."GroupId", m."PlayedAt", m."PlaceName", 6, m."TeamAGoals", m."TeamBGoals",
       m."TeamAColorId", m."TeamBColorId", m."ActualStartTime", NULL, m."CreateDate", now(),
       'bac18efd-395e-537c-8dff-122e7e273c57', ta.team_id, tb.team_id, j.ordem, NULL
FROM imp_jogos j
JOIN "Matches" m ON m."Id" = j.match_id
JOIN imp_times ta ON ta.cor = j.cor_a
JOIN imp_times tb ON tb.cor = j.cor_b
ON CONFLICT ("Id") DO NOTHING;

-- Só quem jogou (time 1 ou 2). Os convites sem time ficam só nas partidas originais.
INSERT INTO "MatchPlayers" ("Id", "MatchId", "PlayerId", "GroupId", "Team", "InviteResponse", "InviteRespondedAt",
                            "IsGoalkeeper", "IsMvp", "DidNotPlay", "VotedForId", "AutoRejectedByAbsenceId", "CreateDate", "UpdateDate")
SELECT md5('campeonato-27-10/jogador/' || mp."Id"::text)::uuid, j.jogo_id, mp."PlayerId", mp."GroupId", mp."Team",
       mp."InviteResponse", mp."InviteRespondedAt", mp."IsGoalkeeper", NULL, false, NULL, NULL, mp."CreateDate", now()
FROM "MatchPlayers" mp
JOIN imp_jogos j ON j.match_id = mp."MatchId"
WHERE mp."Team" IN (1, 2)
ON CONFLICT ("Id") DO NOTHING;

-- ── Trava de saída ──────────────────────────────────────────────────────────
DO $$
DECLARE n int;
BEGIN
  IF (SELECT count(*) FROM "ChampionshipPlayers" WHERE "ChampionshipId" = 'bac18efd-395e-537c-8dff-122e7e273c57') <> 15 THEN
    RAISE EXCEPTION 'Trava: o campeonato não ficou com 15 participantes.';
  END IF;
  IF EXISTS (SELECT 1 FROM "ChampionshipPlayers" WHERE "ChampionshipId" = 'bac18efd-395e-537c-8dff-122e7e273c57'
             GROUP BY "TeamId" HAVING count(*) <> 5 OR count(*) FILTER (WHERE "IsGoalkeeper") <> 1) THEN
    RAISE EXCEPTION 'Trava: algum time não ficou com 5 jogadores e 1 goleiro.';
  END IF;
  SELECT count(*) INTO n FROM "Matches"
  WHERE "ChampionshipId" = 'bac18efd-395e-537c-8dff-122e7e273c57' AND "Status" = 6;
  IF n <> 3 THEN
    RAISE EXCEPTION 'Trava: esperava 3 jogos no campeonato, achei %.', n;
  END IF;
  SELECT count(*) INTO n FROM "MatchPlayers" mp JOIN "Matches" m ON m."Id" = mp."MatchId"
  WHERE m."ChampionshipId" = 'bac18efd-395e-537c-8dff-122e7e273c57';
  IF n <> 30 THEN
    RAISE EXCEPTION 'Trava: esperava 30 jogadores nos 3 jogos novos, achei %.', n;
  END IF;
  -- As originais continuam exatamente como estavam (Ended, fora do campeonato).
  SELECT count(*) INTO n FROM "Matches" m JOIN imp_jogos j ON j.match_id = m."Id"
  WHERE m."Status" = 4 AND m."ChampionshipId" IS NULL;
  IF n <> 3 THEN
    RAISE EXCEPTION 'Trava: as partidas originais foram alteradas.';
  END IF;
END $$;

COMMIT;
