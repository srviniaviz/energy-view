# Energy View

Monitor de potência dos sensores internos e histórico de energia em kWh na bandeja do Windows.

## Instalação

Baixe o instalador x64 da [release mais recente](https://github.com/srviniaviz/energy-view/releases/latest). O app começa a ler os sensores quando abre. A leitura da CPU usa o [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) e o driver oficial [PawnIO](https://pawnio.eu/). Se o driver estiver ausente, o painel oferece a instalação.

Em **Configurações**, use **Iniciar com Windows** para abrir o monitor na bandeja após entrar na sua conta. O app verifica a publicação mais recente ao abrir e mostra o resultado em **Atualizações**. Use **Verificar** para consultar novamente ou **Atualizar** para baixar, validar e abrir o instalador.

A aba **Energia acumulada** soma os kWh das leituras de CPU e GPU válidas enquanto o app roda. O histórico fica em `%LOCALAPPDATA%\EnergyView\energy-history.json`.

Nessa aba, use **Definir tarifa** para cadastrar o valor em R$/kWh e **Monitores** para registrar a potência em watts das telas. As telas marcadas como ligadas entram na estimativa enquanto o app roda. A tarifa vigente calcula o custo sobre o histórico registrado.

O seletor **Plano de energia** aplica o plano no Windows. **Máxima economia** cria um plano baseado no modo de economia com estado máximo da CPU em 70%. **Econômico**, **Médio** e **Desempenho máximo** usam os planos Economia de energia, Equilibrado e Alto desempenho do Windows.

## Build e publicação

Use `dotnet build -c Release` com o SDK .NET 10. Um push na branch `main` dispara o workflow **Windows release**, que calcula a versão, gera o instalador com Inno Setup e publica a tag e a release correspondentes.
