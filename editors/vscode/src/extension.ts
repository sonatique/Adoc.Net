import { workspace, ExtensionContext } from 'vscode';
import {
  LanguageClient,
  LanguageClientOptions,
  ServerOptions,
} from 'vscode-languageclient/node';

let client: LanguageClient | undefined;

export function activate(context: ExtensionContext): void {
  const config = workspace.getConfiguration('adocnet');
  const serverPath = config.get<string>('lsp.path', 'adocnet-lsp');

  const serverOptions: ServerOptions = {
    command: serverPath,
    args: [],
    options: { shell: false },
  };

  const clientOptions: LanguageClientOptions = {
    documentSelector: [
      { scheme: 'file', language: 'asciidoc' },
      { scheme: 'untitled', language: 'asciidoc' },
    ],
    synchronize: {
      fileEvents: workspace.createFileSystemWatcher('**/*.adoc'),
    },
  };

  client = new LanguageClient(
    'adocnet',
    'AdocNet Language Server',
    serverOptions,
    clientOptions
  );

  client.start();
}

export function deactivate(): Thenable<void> | undefined {
  return client?.stop();
}
