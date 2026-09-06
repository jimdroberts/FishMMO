using FishNet.CodeGenerating.Helping;
using FishNet.CodeGenerating.Helping.Extension;
using MonoFN.Cecil;
using MonoFN.Cecil.Cil;
using System.Collections.Generic;

namespace FishNet.CodeGenerating.Extension
{
    internal static class MethodDefinitionExtensions
    {
        public const MethodAttributes PUBLIC_VIRTUAL_ATTRIBUTES = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig;
        public const MethodAttributes PROTECTED_VIRTUAL_ATTRIBUTES = MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig;

        /// <summary>
        /// Returns a custom attribute.
        /// </summary>
        public static CustomAttribute GetCustomAttribute(this MethodDefinition md, string attributeFullName)
        {
            if (md == null)
                return null;

            foreach (CustomAttribute item in md.CustomAttributes)
            {
                if (item.AttributeType.FullName == attributeFullName)
                    return item;
            }

            // Not found.
            return null;
        }

        /// <summary>
        /// Clears the method content and returns ret.
        /// </summary>
        internal static void ClearMethodWithRet(this MethodDefinition md, CodegenSession session, ModuleDefinition importReturnModule = null)
        {
            md.Body.Instructions.Clear();
            ILProcessor processor = md.Body.GetILProcessor();
            processor.Add(session.GetClass<GeneralHelper>().CreateRetDefault(md, importReturnModule));
        }

        /// <summary>
        /// Returns the ParameterDefinition index from end of parameters.
        /// </summary>
        /// <param name = "md"></param>
        /// <param name = "index"></param>
        /// <returns></returns>
        internal static ParameterDefinition GetEndParameter(this MethodDefinition md, int index)
        {
            // Not enough parameters.
            if (md.Parameters.Count < index + 1)
                return null;

            return md.Parameters[md.Parameters.Count - (index + 1)];
        }

        /// <summary>
        /// Creates a variable type within the body and returns it's VariableDef.
        /// </summary>
        internal static VariableDefinition CreateVariable(this MethodDefinition methodDef, TypeReference variableTypeRef)
        {
            VariableDefinition variableDef = new(variableTypeRef);
            methodDef.Body.Variables.Add(variableDef);
            return variableDef;
        }

        /// <summary>
        /// Creates a variable type within the body and returns it's VariableDef.
        /// </summary>
        internal static VariableDefinition CreateVariable(this MethodDefinition methodDef, CodegenSession session, System.Type variableType)
        {
            return CreateVariable(methodDef, session.GetClass<GeneralHelper>().GetTypeReference(variableType));
        }

        /// <summary>
        /// Returns the proper OpCode to use for call methods.
        /// </summary>
        public static OpCode GetCallOpCode(this MethodDefinition md)
        {
            if (md.Attributes.HasFlag(MethodAttributes.Virtual))
                return OpCodes.Callvirt;
            else
                return OpCodes.Call;
        }

        /// <summary>
        /// Returns the proper OpCode to use for call methods.
        /// </summary>
        public static OpCode GetCallOpCode(this MethodReference mr, CodegenSession session)
        {
            return mr.CachedResolve(session).GetCallOpCode();
        }

        /// <summary>
        /// Adds a parameter and returns added parameters.
        /// </summary>
        public static ParameterDefinition CreateParameter(this MethodDefinition thisMd, CodegenSession session, ParameterAttributes attr, System.Type type)
        {
            TypeReference parameterTypeRef = session.ImportReference(type);
            ParameterDefinition pd = new($"p{thisMd.Parameters.Count}", attr, parameterTypeRef);
            thisMd.Parameters.Add(pd);
            return pd;
        }

        /// <summary>
        /// Adds otherMd parameters to thisMd and returns added parameters.
        /// </summary>
        public static List<ParameterDefinition> CreateParameters(this MethodDefinition thisMd, CodegenSession session, MethodDefinition otherMd)
        {
            List<ParameterDefinition> results = new();

            foreach (ParameterDefinition pd in otherMd.Parameters)
            {
                /* FISHMMO EDIT (issue #229): the parameter type must be IMPORTED into this module, not
                 * borrowed from otherMd's module. Cecil resolves a parameter's lazily-loaded custom
                 * attributes through parameterType.Module, and the writer assigns the new Param row id
                 * BEFORE it asks HasCustomAttributes. With a foreign type reference that lookup lands
                 * in the OTHER assembly's CustomAttribute table at whatever row this parameter happens
                 * to receive here; when that row is an `in` parameter over there, its [IsReadOnly]
                 * attribute (constructor owned by the other module) is attached and the write fails with
                 * "Member 'IsReadOnlyAttribute::.ctor()' is declared in another module and needs to be
                 * imported". Whether the rows collide depends on the parameter counts of both assemblies,
                 * which is why the failure appeared and vanished with unrelated code changes and differed
                 * between platforms. */
                TypeReference parameterTypeRef = session.ImportReference(pd.ParameterType);
                int currentCount = thisMd.Parameters.Count;
                string name = pd.Name + currentCount;
                ParameterDefinition parameterDef = new(name, pd.Attributes, parameterTypeRef);
                // Set any default values.
                parameterDef.Constant = pd.Constant;
                parameterDef.IsReturnValue = pd.IsReturnValue;
                parameterDef.IsOut = pd.IsOut;
                /* FISHMMO EDIT (issue #229): materialise the attribute list while the parameter still has
                 * row id 0 (a guaranteed miss), so the writer can never lazily read a colliding row later.
                 * Copied attributes are cloned with an imported constructor rather than shared. */
                MonoFN.Collections.Generic.Collection<CustomAttribute> parameterAttributes = parameterDef.CustomAttributes;
                foreach (CustomAttribute item in pd.CustomAttributes)
                    parameterAttributes.Add(new(session.ImportReference(item.Constructor), item.GetBlob()));
                parameterDef.HasConstant = pd.HasConstant;
                parameterDef.HasDefault = pd.HasDefault;

                if (parameterDef == null || thisMd.Parameters == null)
                {
                    session.LogError($"ParameterDefinition or collection is null. Definition null: {parameterDef == null}. Collection null: {thisMd.Parameters == null}.");
                }
                else
                {
                    thisMd.Parameters.Add(parameterDef);
                    results.Add(parameterDef);
                }
            }

            return results;
        }

        /// <summary>
        /// Returns a method reference while considering if declaring type is generic.
        /// </summary>
        public static MethodReference GetMethodReference(this MethodDefinition md, CodegenSession session)
        {
            MethodReference methodRef = session.ImportReference(md);

            // Is generic.
            if (md.DeclaringType.HasGenericParameters)
            {
                GenericInstanceType git = methodRef.DeclaringType.MakeGenericInstanceType();
                MethodReference result = new(md.Name, md.ReturnType)
                {
                    HasThis = md.HasThis,
                    ExplicitThis = md.ExplicitThis,
                    DeclaringType = git,
                    CallingConvention = md.CallingConvention
                };
                foreach (ParameterDefinition pd in md.Parameters)
                {
                    session.ImportReference(pd.ParameterType);
                    result.Parameters.Add(pd);
                }
                return result;
            }
            else
            {
                return methodRef;
            }
        }

        /// <summary>
        /// Returns a method reference for a generic method.
        /// </summary>
        public static MethodReference GetMethodReference(this MethodDefinition md, CodegenSession session, TypeReference typeReference)
        {
            MethodReference methodRef = session.ImportReference(md);
            return methodRef.GetMethodReference(session, typeReference);
        }

        /// <summary>
        /// Removes ret if it exist at the end of the method. Returns if ret was removed.
        /// </summary>
        internal static bool RemoveEndRet(this MethodDefinition md, CodegenSession session)
        {
            int count = md.Body.Instructions.Count;
            if (count > 0 && md.Body.Instructions[count - 1].OpCode == OpCodes.Ret)
            {
                md.Body.Instructions.RemoveAt(count - 1);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a method reference for a generic method.
        /// </summary>
        public static MethodReference GetMethodReference(this MethodDefinition md, CodegenSession session, TypeReference[] typeReferences)
        {
            MethodReference methodRef = session.ImportReference(md);
            return methodRef.GetMethodReference(session, typeReferences);
        }

        public static MethodDefinition CreateCopy(this MethodDefinition copiedMd, CodegenSession session, string nameOverride = null, MethodAttributes? attributesOverride = null)
        {
            // FISHMMO EDIT (issue #229): use the imported return type; the original discarded the import result.
            TypeReference returnTypeRef = session.ImportReference(copiedMd.ReturnType);

            MethodAttributes attr = attributesOverride.HasValue ? attributesOverride.Value : copiedMd.Attributes;
            string name = nameOverride == null ? copiedMd.Name : nameOverride;
            MethodDefinition result = new(name, attr, returnTypeRef);
            foreach (GenericParameter item in copiedMd.GenericParameters)
                result.GenericParameters.Add(item);

            result.CreateParameters(session, copiedMd);
            return result;
        }

        /// <summary>
        /// Makes a method definition public.
        /// </summary>
        public static void SetPublicAttributes(this MethodDefinition md)
        {
            md.Attributes = PUBLIC_VIRTUAL_ATTRIBUTES;
        }

        public static void SetProtectedAttributes(this MethodDefinition md)
        {
            md.Attributes = PROTECTED_VIRTUAL_ATTRIBUTES;
        }
    }
}