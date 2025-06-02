using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Api24ContentAI.Domain.Service;

namespace Api24ContentAI.Infrastructure.Service.Implementations;

public class FileProcessorFactory : IFileProcessorFactory
{
    
    private readonly IEnumerable<IFileProcessor> _processors;
    
    public FileProcessorFactory(IEnumerable<IFileProcessor> processors)
    {
        _processors = processors;
    }

    public IFileProcessor GetProcessor(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var processor = _processors.FirstOrDefault(p => p.CanProcess(extension));

        if (processor == null)
        {
            throw new NotSupportedException($"File extension {extension} is not supported");
        }

        return processor;
    }
    
}