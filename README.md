# Energy View

Monitor de potência dos sensores internos e histórico de energia em kWh na bandeja do Windows.

## Instalação

Baixe o instalador x64 da [release mais recente](https://github.com/srviniaviz/energy-view/releases/latest). O app começa a ler os sensores quando abre. A leitura da CPU usa o [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) e o driver oficial [PawnIO](https://pawnio.eu/). Se o driver estiver ausente, o painel oferece a instalação.

Use **Iniciar com Windows** para abrir o monitor na bandeja após entrar na sua conta. O app instalado consulta as releases do GitHub automaticamente e avisa quando há uma atualização. Para consultar na hora, clique com o botão direito no ícone da bandeja e escolha **Verificar atualizações**.

A aba **Energia acumulada** soma os kWh das leituras de CPU e GPU válidas enquanto o app roda. O histórico fica em `%LOCALAPPDATA%\EnergyView\energy-history.json`.

## Build e publicação

Use `dotnet build -c Release` com o SDK .NET 10. Uma tag estável no formato `v1.2.3` dispara o workflow **Windows release**, que compila o app, gera o instalador com Inno Setup e publica o executável na release correspondente.
