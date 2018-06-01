using System;
using Android.Content;
using Android.Graphics;
using Android.Opengl;
using Java.Nio;

namespace XamarinFormsAR.Droid
{
    public class ObjectRenderer
    {

        const string TAG = "OBJECTRENDERER";

        public enum BlendMode
        {
            Null,
            Shadow,
            Grid
        };

        const int COORDS_PER_VERTEX = 3;

        static readonly float[] LIGHT_DIRECTION = { 0.0f, 1.0f, 0.0f, 0.0f };
        float[] mViewLightDirection = new float[4];

        int mVertexBufferId;
        int mVerticesBaseAddress;
        int mTexCoordsBaseAddress;
        int mNormalsBaseAddress;
        int mIndexBufferId;
        int mIndexCount;

        int mProgram;
        int[] mTextures = new int[1];

        int mModelViewUniform;
        int mModelViewProjectionUniform;

        int mPositionAttribute;
        int mNormalAttribute;
        int mTexCoordAttribute;

        int mTextureUniform;

        int mLightingParametersUniform;

        int mMaterialParametersUniform;

        BlendMode mBlendMode = BlendMode.Null;

        float[] mModelMatrix = new float[16];
        float[] mModelViewMatrix = new float[16];
        float[] mModelViewProjectionMatrix = new float[16];

        float mAmbient = 0.3f;
        float mDiffuse = 1.0f;
        float mSpecular = 1.0f;
        float mSpecularPower = 6.0f;

        public ObjectRenderer()
        {
        }

        public void CreateOnGlThread(Context context, string objAssetName, string diffuseTextureAssetName)
        {
            // Read the texture.
            var textureBitmap = BitmapFactory.DecodeStream(context.Assets.Open(diffuseTextureAssetName));

            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlGenTextures(mTextures.Length, mTextures, 0);
            GLES20.GlBindTexture(GLES20.GlTexture2d, mTextures[0]);

            GLES20.GlTexParameteri(GLES20.GlTexture2d,
                GLES20.GlTextureMinFilter, GLES20.GlLinearMipmapLinear);
            GLES20.GlTexParameteri(GLES20.GlTexture2d,
                GLES20.GlTextureMagFilter, GLES20.GlLinear);
            GLUtils.TexImage2D(GLES20.GlTexture2d, 0, textureBitmap, 0);
            GLES20.GlGenerateMipmap(GLES20.GlTexture2d);
            GLES20.GlBindTexture(GLES20.GlTexture2d, 0);

            textureBitmap.Recycle();

            ShaderUtil.CheckGLError(TAG, "Texture loading");

            // Read the obj file.
            var objInputStream = context.Assets.Open(objAssetName);
            var obj = JavaGl.Obj.ObjReader.Read(objInputStream);

            obj = JavaGl.Obj.ObjUtils.ConvertToRenderable(obj);
            
            IntBuffer wideIndices = JavaGl.Obj.ObjData.GetFaceVertexIndices(obj, 3);
            FloatBuffer vertices = JavaGl.Obj.ObjData.GetVertices(obj);
            FloatBuffer texCoords = JavaGl.Obj.ObjData.GetTexCoords(obj, 2);
            FloatBuffer normals = JavaGl.Obj.ObjData.GetNormals(obj);

            ShortBuffer indices = ByteBuffer.AllocateDirect(2 * wideIndices.Limit())
                .Order(ByteOrder.NativeOrder()).AsShortBuffer();
            while (wideIndices.HasRemaining)
            {
                indices.Put((short)wideIndices.Get());
            }
            indices.Rewind();

            var buffers = new int[2];
            GLES20.GlGenBuffers(2, buffers, 0);
            mVertexBufferId = buffers[0];
            mIndexBufferId = buffers[1];

            mVerticesBaseAddress = 0;
            mTexCoordsBaseAddress = mVerticesBaseAddress + 4 * vertices.Limit();
            mNormalsBaseAddress = mTexCoordsBaseAddress + 4 * texCoords.Limit();
            int totalBytes = mNormalsBaseAddress + 4 * normals.Limit();

            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, mVertexBufferId);
            GLES20.GlBufferData(GLES20.GlArrayBuffer, totalBytes, null, GLES20.GlStaticDraw);
            GLES20.GlBufferSubData(
                GLES20.GlArrayBuffer, mVerticesBaseAddress, 4 * vertices.Limit(), vertices);
            GLES20.GlBufferSubData(
                GLES20.GlArrayBuffer, mTexCoordsBaseAddress, 4 * texCoords.Limit(), texCoords);
            GLES20.GlBufferSubData(
                GLES20.GlArrayBuffer, mNormalsBaseAddress, 4 * normals.Limit(), normals);
            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);

            GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, mIndexBufferId);
            mIndexCount = indices.Limit();
            GLES20.GlBufferData(
                GLES20.GlElementArrayBuffer, 2 * mIndexCount, indices, GLES20.GlStaticDraw);
            GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, 0);

            ShaderUtil.CheckGLError(TAG, "OBJ buffer load");

            int vertexShader = ShaderUtil.LoadGLShader(TAG, context,
                    GLES20.GlVertexShader, Resource.Raw.object_vertex);
            int fragmentShader = ShaderUtil.LoadGLShader(TAG, context,
                GLES20.GlFragmentShader, Resource.Raw.object_fragment);

            mProgram = GLES20.GlCreateProgram();
            GLES20.GlAttachShader(mProgram, vertexShader);
            GLES20.GlAttachShader(mProgram, fragmentShader);
            GLES20.GlLinkProgram(mProgram);
            GLES20.GlUseProgram(mProgram);

            ShaderUtil.CheckGLError(TAG, "Program creation");

            mModelViewUniform = GLES20.GlGetUniformLocation(mProgram, "u_ModelView");
            mModelViewProjectionUniform =
                GLES20.GlGetUniformLocation(mProgram, "u_ModelViewProjection");

            mPositionAttribute = GLES20.GlGetAttribLocation(mProgram, "a_Position");
            mNormalAttribute = GLES20.GlGetAttribLocation(mProgram, "a_Normal");
            mTexCoordAttribute = GLES20.GlGetAttribLocation(mProgram, "a_TexCoord");

            mTextureUniform = GLES20.GlGetUniformLocation(mProgram, "u_Texture");

            mLightingParametersUniform = GLES20.GlGetUniformLocation(mProgram, "u_LightingParameters");
            mMaterialParametersUniform = GLES20.GlGetUniformLocation(mProgram, "u_MaterialParameters");

            ShaderUtil.CheckGLError(TAG, "Program parameters");

            Android.Opengl.Matrix.SetIdentityM(mModelMatrix, 0);
        }

        public void SetBlendMode(BlendMode blendMode)
        {
            mBlendMode = blendMode;
        }

        public void updateModelMatrix(float[] modelMatrix, float scaleFactor)
        {
            float[] scaleMatrix = new float[16];
            Android.Opengl.Matrix.SetIdentityM(scaleMatrix, 0);
            scaleMatrix[0] = scaleFactor;
            scaleMatrix[5] = scaleFactor;
            scaleMatrix[10] = scaleFactor;
            Android.Opengl.Matrix.MultiplyMM(mModelMatrix, 0, modelMatrix, 0, scaleMatrix, 0);
        }

        public void setMaterialProperties(
                float ambient, float diffuse, float specular, float specularPower)
        {
            mAmbient = ambient;
            mDiffuse = diffuse;
            mSpecular = specular;
            mSpecularPower = specularPower;
        }

        public void Draw(float[] cameraView, float[] cameraPerspective, float lightIntensity)
        {

            ShaderUtil.CheckGLError(TAG, "Before draw");

            Android.Opengl.Matrix.MultiplyMM(mModelViewMatrix, 0, cameraView, 0, mModelMatrix, 0);
            Android.Opengl.Matrix.MultiplyMM(mModelViewProjectionMatrix, 0, cameraPerspective, 0, mModelViewMatrix, 0);

            GLES20.GlUseProgram(mProgram);

            Android.Opengl.Matrix.MultiplyMV(mViewLightDirection, 0, mModelViewMatrix, 0, LIGHT_DIRECTION, 0);
            normalizeVec3(mViewLightDirection);
            GLES20.GlUniform4f(mLightingParametersUniform,
                mViewLightDirection[0], mViewLightDirection[1], mViewLightDirection[2], lightIntensity);

            GLES20.GlUniform4f(mMaterialParametersUniform, mAmbient, mDiffuse, mSpecular,
                mSpecularPower);

            GLES20.GlActiveTexture(GLES20.GlTexture0);
            GLES20.GlBindTexture(GLES20.GlTexture2d, mTextures[0]);
            GLES20.GlUniform1i(mTextureUniform, 0);

            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, mVertexBufferId);

            GLES20.GlVertexAttribPointer(
                mPositionAttribute, COORDS_PER_VERTEX, GLES20.GlFloat, false, 0, mVerticesBaseAddress);
            GLES20.GlVertexAttribPointer(
                mNormalAttribute, 3, GLES20.GlFloat, false, 0, mNormalsBaseAddress);
            GLES20.GlVertexAttribPointer(
                mTexCoordAttribute, 2, GLES20.GlFloat, false, 0, mTexCoordsBaseAddress);

            GLES20.GlBindBuffer(GLES20.GlArrayBuffer, 0);

            GLES20.GlUniformMatrix4fv(
                mModelViewUniform, 1, false, mModelViewMatrix, 0);
            GLES20.GlUniformMatrix4fv(
                mModelViewProjectionUniform, 1, false, mModelViewProjectionMatrix, 0);

            GLES20.GlEnableVertexAttribArray(mPositionAttribute);
            GLES20.GlEnableVertexAttribArray(mNormalAttribute);
            GLES20.GlEnableVertexAttribArray(mTexCoordAttribute);

            if (mBlendMode != BlendMode.Null)
            {
                GLES20.GlDepthMask(false);
                GLES20.GlEnable(GLES20.GlBlend);
                switch (mBlendMode)
                {
                    case BlendMode.Shadow:
                        GLES20.GlBlendFunc(GLES20.GlZero, GLES20.GlOneMinusSrcAlpha);
                        break;
                    case BlendMode.Grid:
                        GLES20.GlBlendFunc(GLES20.GlSrcAlpha, GLES20.GlOneMinusSrcAlpha);
                        break;
                }
            }

            GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, mIndexBufferId);
            GLES20.GlDrawElements(GLES20.GlTriangles, mIndexCount, GLES20.GlUnsignedShort, 0);
            GLES20.GlBindBuffer(GLES20.GlElementArrayBuffer, 0);

            if (mBlendMode != BlendMode.Null)
            {
                GLES20.GlDisable(GLES20.GlBlend);
                GLES20.GlDepthMask(true);
            }

            // Disable vertex arrays
            GLES20.GlDisableVertexAttribArray(mPositionAttribute);
            GLES20.GlDisableVertexAttribArray(mNormalAttribute);
            GLES20.GlDisableVertexAttribArray(mTexCoordAttribute);

            GLES20.GlBindTexture(GLES20.GlTexture2d, 0);

            ShaderUtil.CheckGLError(TAG, "After draw");
        }

        public static void normalizeVec3(float[] v)
        {
            float reciprocalLength = 1.0f / (float)Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            v[0] *= reciprocalLength;
            v[1] *= reciprocalLength;
            v[2] *= reciprocalLength;
        }
    }
}