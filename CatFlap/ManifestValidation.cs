using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;

namespace Catflap
{
    public class ValidationException : Exception { public ValidationException(string message) : base(message) { } };
    
    public partial class Manifest
    {
        public void Validate(string rootPath)
        {
            // Basic sanity checks for all sync items
            foreach (var syncItem in this.sync)
            {
                var fullPath = (rootPath + "/" + syncItem.name).NormalizePath();

                if (fullPath.EndsWith(".catflap"))
                    throw new ValidationException("não foi possível sincronizar itens no diretório do chimera " + syncItem.name);

                if (fullPath == rootPath)
                    throw new ValidationException("não é possível sincronizar o diretório raiz diretamente em " + syncItem.name);

                if (!fullPath.StartsWith(rootPath))
                    throw new ValidationException("iria colocar itens sicnronizados fora da raiz: " + syncItem.name);

                if (syncItem.type != "delete" && syncItem.type != "rsync")
                    throw new ValidationException("tipo de sincronização inválida: " + syncItem.type + " para " + syncItem.name);
            }

            if (!this.baseUrl.StartsWith("http://") && !this.baseUrl.StartsWith("https://"))
                throw new ValidationException("baseUrl precisa começar com http(s)://");
            if (!this.rsyncUrl.StartsWith("rsync://"))
                throw new ValidationException("rsyncUrl não contém rsync://");

            if (this.version != Manifest.VERSION)
                throw new ValidationException("Seu chimera.exe é de uma versão diferente da usada neste servidor (Esperada: " +
                    this.version + ", a sua: " + Manifest.VERSION + "). Por favor, verifique se você está com a versão correta.");

            if (this.ignoreCase.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.ignoreCase.HasValue)
                        syncItem.ignoreCase = this.ignoreCase.Value;

            if (this.fuzzy.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.fuzzy.HasValue)
                        syncItem.fuzzy = this.fuzzy.Value;

            if (this.ignoreExisting.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.ignoreExisting.HasValue)
                        syncItem.ignoreExisting = this.ignoreExisting.Value;

            if (this.purge.HasValue)
                foreach (var syncItem in this.sync)
                    if (!syncItem.purge.HasValue)
                        syncItem.purge = this.purge.Value;
        }
    }
}
